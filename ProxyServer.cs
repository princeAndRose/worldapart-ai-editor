using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace WorldApartAI {
 public sealed class RequestRecord {
  public string Id=Guid.NewGuid().ToString("N").Substring(0,8),Model="",Status="转发中",Before="",After="",Rules="";
  public DateTime Time=DateTime.Now;public long Milliseconds;public int Characters;
  public override string ToString(){return Time.ToString("HH:mm:ss")+" · "+Status+"\n"+Model+" · "+(Milliseconds/1000.0).ToString("0.0")+" 秒";}
 }
 public sealed class ProxyServer : IDisposable {
  readonly object sync=new object();TcpListener listener;Thread acceptThread;StudioSettings settings;bool running;int generation,runId;
  readonly HashSet<TcpClient> clients=new HashSet<TcpClient>();readonly HashSet<HttpWebRequest> upstreams=new HashSet<HttpWebRequest>();
  readonly List<RequestRecord> records=new List<RequestRecord>();public int Revision{get;private set;}
  public bool Running{get{lock(sync)return running;}}
  public static Uri ValidateUpstream(string value,int port){
   Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||!(uri.Scheme=="https"||uri.Scheme=="http"))throw new InvalidDataException("模型服务地址需为 http(s) 地址，不能含密钥、查询参数或片段。");
   IPAddress ip;bool local=uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||(IPAddress.TryParse(uri.Host,out ip)&&IPAddress.IsLoopback(ip));
   if(uri.Scheme=="http"&&!local)throw new InvalidDataException("远程模型服务请使用 HTTPS；HTTP 仅支持本机地址。");
   if(local&&uri.Port==port)throw new InvalidDataException("上游不能指向代理自身端口。");
   return uri;
  }
  public void Configure(StudioSettings value){StudioLibrary.Validate(value);lock(sync){if(running&&settings.Port!=value.Port)throw new InvalidOperationException("请停止代理后再修改监听端口。");settings=Json.Read<StudioSettings>(Json.Write(value));if(!value.CaptureRequests)ClearLocked();}}
  public void Start(StudioSettings value){
   if(Running)throw new InvalidOperationException("代理已经启动。");Configure(value);
   var next=new TcpListener(IPAddress.Loopback,value.Port);next.Server.ExclusiveAddressUse=true;next.Start(8);
   int run;lock(sync){listener=next;running=true;run=++runId;}acceptThread=new Thread(delegate(){Accept(next,run);}){IsBackground=true};acceptThread.Start();
  }
  void Accept(TcpListener owner,int run){while(Running){TcpClient client;try{client=owner.AcceptTcpClient();}catch{return;}lock(sync){if(!running||runId!=run||listener!=owner||clients.Count>=8){client.Close();continue;}clients.Add(client);}ThreadPool.QueueUserWorkItem(delegate{Handle(client,run);});}}
  public List<RequestRecord> Snapshot(){lock(sync)return records.Select(r=>Json.Read<RequestRecord>(Json.Write(r))).ToList();}
  void ClearLocked(){generation++;records.Clear();Revision++;}
  public void Clear(){lock(sync)ClearLocked();}
  public void Stop(){lock(sync){running=false;runId++;if(listener!=null)listener.Stop();foreach(var r in upstreams)r.Abort();foreach(var c in clients)c.Close();}}
  public void Dispose(){Stop();Clear();}
  static string Header(NetworkStream stream){var b=new List<byte>();while(b.Count<16384){int n=stream.ReadByte();if(n<0)throw new IOException();b.Add((byte)n);int c=b.Count;if(c>=4&&b[c-4]==13&&b[c-3]==10&&b[c-2]==13&&b[c-1]==10)return Encoding.ASCII.GetString(b.ToArray());}throw new InvalidDataException("请求头过大。");}
  static void Error(Stream output,int code,string message){byte[] body=Encoding.UTF8.GetBytes(Json.Write(new{error=new{message=message,type="editor_proxy"}}));byte[] head=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" Error\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");output.Write(head,0,head.Length);output.Write(body,0,body.Length);}
  void Handle(TcpClient client,int run){
   HttpWebRequest request=null;RequestRecord record=null;int epoch;StudioSettings config;lock(sync){if(!running||runId!=run){clients.Remove(client);client.Close();return;}config=settings;epoch=generation;}var watch=Stopwatch.StartNew();bool responseStarted=false;
   try{client.ReceiveTimeout=15000;client.SendTimeout=30000;var incoming=client.GetStream();{
    var lines=Header(incoming).Split(new[]{"\r\n"},StringSplitOptions.None);var first=lines[0].Split(' ');
    if(first.Length!=3)throw new InvalidDataException("请求行无效。");
    var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var line in lines.Skip(1).Where(l=>l.Length>0)){int colon=line.IndexOf(':');if(colon<1||headers.ContainsKey(line.Substring(0,colon)))throw new InvalidDataException("请求头重复或无效。");headers.Add(line.Substring(0,colon),line.Substring(colon+1).Trim());}
    string host;if(!headers.TryGetValue("Host",out host)||!(host=="127.0.0.1:"+config.Port||host=="localhost:"+config.Port)||headers.ContainsKey("Origin")){Error(incoming,403,"仅接收本机 AI 客户端请求。");return;}
    bool chat=first[0]=="POST"&&first[1]=="/v1/chat/completions",models=first[0]=="GET"&&first[1]=="/v1/models";
    if(!chat&&!models){Error(incoming,404,"支持 POST /v1/chat/completions 和 GET /v1/models。");return;}
    if(headers.ContainsKey("Transfer-Encoding")||headers.ContainsKey("Content-Encoding")){Error(incoming,415,"请求需使用未压缩、带 Content-Length 的 JSON。");return;}
    string lengthText;int length=0;if(headers.TryGetValue("Content-Length",out lengthText)&&(!Int32.TryParse(lengthText,out length)||length<0||length>2097152)){Error(incoming,413,"请求最多 2 MiB。");return;}
    if(chat&&length==0){Error(incoming,411,"需要 Content-Length。");return;}
    if(headers.ContainsKey("Expect")){byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");incoming.Write(interim,0,interim.Length);}
    var bytes=new byte[length];for(int pos=0;pos<length;){int read=incoming.Read(bytes,pos,length-pos);if(read==0)throw new IOException();pos+=read;}
    byte[] outgoing=bytes;if(chat){string body=new UTF8Encoding(false,true).GetString(bytes);var trace=PromptTrace.Build(body,config);outgoing=Encoding.UTF8.GetBytes(trace.After);
     if(config.CaptureRequests){var obj=Json.Obj(body);record=new RequestRecord{Model=obj.ContainsKey("model")?Convert.ToString(obj["model"]):"未指定模型",Before=trace.Before,After=trace.After,Characters=trace.AddedCharacters,Rules=String.Join("\n",trace.Decisions)+"\n\n实际加入正文：\n"+trace.Injected};
      lock(sync){if(epoch==generation){records.Insert(0,record);while(records.Count>20||records.Sum(r=>(long)r.Before.Length+r.After.Length)>8000000)records.RemoveAt(records.Count-1);Revision++;}}
     }
    }
    Uri upstream=ValidateUpstream(config.Upstream,config.Port);var target=new Uri(upstream.AbsoluteUri.TrimEnd('/')+(chat?"/chat/completions":"/models"));
    // Reject DNS aliases resolving back to this listener too.
    if(target.Port==config.Port&&Dns.GetHostAddresses(target.Host).Any(IPAddress.IsLoopback))throw new InvalidDataException("上游解析到了代理自身。");
    request=(HttpWebRequest)WebRequest.Create(target);request.Method=first[0];request.AllowAutoRedirect=false;request.Proxy=null;request.Timeout=config.TimeoutSeconds*1000;request.ReadWriteTimeout=config.TimeoutSeconds*1000;request.KeepAlive=false;
    request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;request.ContentType="application/json";request.Accept="application/json, text/event-stream";
    string auth;if(headers.TryGetValue("Authorization",out auth))request.Headers["Authorization"]=auth;
    lock(sync){if(!running||runId!=run)throw new IOException();upstreams.Add(request);}
    using(var deadline=new Timer(delegate{try{request.Abort();client.Close();}catch{}},null,config.TimeoutSeconds*1000,Timeout.Infinite)){
     if(chat){request.ContentLength=outgoing.Length;using(var output=request.GetRequestStream())output.Write(outgoing,0,outgoing.Length);}
     HttpWebResponse response;try{response=(HttpWebResponse)request.GetResponse();}catch(WebException ex){response=ex.Response as HttpWebResponse;if(response==null)throw;}
     using(response){int status=(int)response.StatusCode;string type=response.ContentType??"application/json";type=type.Replace("\r","").Replace("\n","");
      byte[] head=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+" Response\r\nContent-Type: "+type+"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");incoming.Write(head,0,head.Length);responseStarted=true;
      using(var source=response.GetResponseStream()){var buffer=new byte[8192];int count;while((count=source.Read(buffer,0,buffer.Length))>0){incoming.Write(buffer,0,count);incoming.Flush();}}
      if(record!=null)record.Status=status>=200&&status<300?"完成":"上游 HTTP "+status;
     }
    }
   }}catch(Exception ex){if(record!=null)record.Status=(!Running||runId!=run)?"已停止":watch.ElapsedMilliseconds>=config.TimeoutSeconds*1000L?"代理超时":ex is InvalidDataException?"请求无效":"连接失败或中断";
    if(!responseStarted)try{Error(client.GetStream(),ex is InvalidDataException?400:502,ex is InvalidDataException?ex.Message:"上游连接失败或请求中断，请检查服务、模型与等待时间。");}catch{}
   }finally{watch.Stop();lock(sync){clients.Remove(client);if(request!=null)upstreams.Remove(request);if(record!=null){record.Milliseconds=watch.ElapsedMilliseconds;Revision++;}}client.Close();}
  }
 }
}
