using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace WorldApartAI {
 public static class StudioTests {
  static void Check(bool condition,string message){if(!condition)throw new Exception("Studio: "+message);}
  static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
  public static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
  public static void Run(string folder,Catalog catalog){
   string root=Path.Combine(folder,"studio-tests");Directory.CreateDirectory(root);var store=new Store(Path.Combine(root,"mod"),catalog,new MemoryPrefs());int id=100000;var plan=CharacterPlan.Capture(store,id,"初始");
   store.SetStyle(id,"zh-Hans","新的风格");string other=store.Style(100001,"zh-Hans",false);plan.Apply(store,id);Check(store.Style(id,"zh-Hans",false)==store.Style(id,"zh-Hans",true),"Plan restores original fields");Check(store.Style(100001,"zh-Hans",false)==other,"Plan preserves other NPC");Reject(delegate{plan.Apply(store,100001);},"Cross NPC rejected");
   var bad=Json.Read<CharacterPlan>(Json.Write(plan));bad.Values.RemoveAt(0);string before=Json.Write(store.ExportPreset());Reject(delegate{bad.Apply(store,id);},"Incomplete plan rejected");Check(Json.Write(store.ExportPreset())==before,"Invalid plan atomic");
   var config=new StudioSettings{Budget=1000,Entries=new List<LoreEntry>{new LoreEntry{Id="a",Name="客栈",Keywords="灯,客栈",SystemMatch="掌柜",Content="有一盏灯",Priority=10},new LoreEntry{Id="b",Name="未命中",Keywords="雪",Content="雪"}}};
   string body=Json.Write(new{model="fixture",stream=false,temperature=0.8,messages=new[]{new{role="system",content="掌柜：保持JSON格式"},new{role="user",content="客栈有灯吗"}}});
   var trace=PromptTrace.Build(body,config);Check(trace.Decisions[0].Contains("已插入"),"keyword hit");var parsed=Json.Obj(trace.After);Check(Json.List(parsed["messages"]).Count==3&&Convert.ToDouble(parsed["temperature"])==0.8,"Original params retained");Check(Json.Dict(Json.List(parsed["messages"])[0])["content"].ToString()=="掌柜：保持JSON格式","Original order retained");
   config.Entries[0].MatchAll=true;config.Entries[0].Keywords="灯,雪";Check(PromptTrace.Build(body,config).After==body,"all condition misses without mutations");config.Entries[0].Always=true;config.Budget=1;Check(PromptTrace.Build(body,config).Decisions[0].Contains("预算"),"budget skips whole entry");config.Budget=1000;config.Entries[0].SystemMatch="另一个人";Check(PromptTrace.Build(body,config).After==body,"system AND condition");
   config.Entries[0].SystemMatch="掌柜";config.Entries[0].Always=false;config.Entries[0].MatchAll=false;config.Entries[0].Keywords="雪";
   string history=Json.Write(new{messages=new[]{new{role="system",content="掌柜"},new{role="user",content="雪"},new{role="assistant",content="雪"},new{role="user",content="灯"}}});Check(PromptTrace.Build(history,config).After==history,"default excludes old history");config.Entries[0].Scope="recent-users";Check(PromptTrace.Build(history,config).After!=history,"recent users scope");
   var library=new StudioLibrary(Path.Combine(root,"studio.json"));library.Save(config);var second=new StudioLibrary(library.PathName);config.Budget=999;library.Save(config);Reject(delegate{second.Save(second.Data);},"Conflict protects library");Check(File.Exists(library.PathName+".bak"),"backup created");
   File.WriteAllText(library.PathName,"{broken");var damaged=new StudioLibrary(library.PathName);Check(damaged.LoadError!=null,"corrupt library isolated");Reject(delegate{damaged.Save(config);},"corrupt file cannot be silently overwritten");damaged.Recover(true);Check(damaged.LoadError==null&&damaged.Data.Budget==1000,"backup recovery");var nullPlan=new StudioSettings();nullPlan.Plans.Add(null);Reject(delegate{StudioLibrary.Validate(nullPlan);},"null plan rejected");
   Reject(delegate{ProxyServer.ValidateUpstream("http://example.com/v1",18181);},"Remote plaintext rejected");Reject(delegate{ProxyServer.ValidateUpstream("http://localhost:18181/v1",18181);},"Loop rejected");Reject(delegate{ProxyServer.ValidateUpstream("https://x.test/v1?key=secret",18181);},"URL secrets rejected");
   PolicyTests(root);NetworkTests(body);
   File.WriteAllText(Path.Combine(folder,"STUDIO-PASSED.txt"),"PASS: plan scope/full restore/atomic rejection; lore keyword all/any/system AND/recent-user scope/budget/no-op/params; library conflict/backup; real loopback proxy HTTP auth/models/errors/SSE/clear/stop/port conflict. Synthetic requests only.");
  }
  // Runs without game resources and never contacts an external model.
  public static void RunProxyTests(string folder){Directory.CreateDirectory(folder);PolicyTests(folder);NetworkTests(Json.Write(new{model="fixture",stream=false,messages=new[]{new{role="user",content="测试"}}}));File.WriteAllText(Path.Combine(folder,"PROXY-PASSED.txt"),"PASS: exact LAN grants/default deny/persistence/revocation; URL policy; loopback HTTP/auth/models/errors/SSE/reconfigure/redirect isolation. Synthetic requests only.");}
  static void PolicyTests(string folder){
   foreach(string host in new[]{"10.0.0.1","10.255.255.254","172.16.0.1","172.31.255.254","192.168.0.235"}){
    string url="http://"+host+":1234/v1",grant="http://"+host+":1234";
    Check(ProxyServer.LanHttpOrigin(url)==grant,"LAN literal candidate "+host);
    Reject(delegate{ProxyServer.ValidateUpstream(url,18181);},"LAN requires opt-in "+host);
    Check(ProxyServer.ValidateUpstream(url,18181,grant).Host==host,"exact LAN grant "+host);
   }
   foreach(string host in new[]{"172.15.255.255","172.32.0.1","192.169.0.1","8.8.8.8","169.254.169.254","100.64.0.1","0.0.0.0","224.0.0.1","lan.example","[fd00::1]","[fe80::1]","[::ffff:192.168.0.235]","3232235755","0xc0a801eb","192.168.491","192.168.001.235"}){
    string url="http://"+host+":1234/v1";Check(ProxyServer.LanHttpOrigin(url)==null,"Non-canonical/non-RFC1918 candidate denied "+host);Reject(delegate{ProxyServer.ValidateUpstream(url,18181,"http://"+host+":1234");},"grant cannot allow arbitrary host "+host);
   }
   const string lan="http://192.168.0.235:1234/v1",origin="http://192.168.0.235:1234";
   foreach(string url in new[]{"http://192.168.0.236:1234/v1","http://192.168.0.235:1235/v1","https://192.168.0.235:1234/v1","http://user:secret@192.168.0.235:1234/v1","http://192.168.0.235:1234/v1?key=secret","http://192.168.0.235:1234/v1#fragment"})Reject(delegate{ProxyServer.ValidateUpstream(url,18181,origin);},"scope or malformed URL rejected "+url);
   Check(ProxyServer.LanHttpOrigin("http://192.168.0.235/v1")=="http://192.168.0.235:80","default port normalized");
   ProxyServer.ValidateUpstream("http://127.0.0.1:1234/v1",18181);ProxyServer.ValidateUpstream("http://localhost:1234/v1",18181);ProxyServer.ValidateUpstream("https://api.example.com/v1",18181);
   Reject(delegate{ProxyServer.ValidateUpstream("http://localhost:18181/v1",18181);},"self-loop rejected");
   var old=Json.Read<StudioSettings>("{\"Version\":1,\"Upstream\":\"http://127.0.0.1:1234/v1\"}");StudioLibrary.Validate(old);Check(String.IsNullOrEmpty(old.TrustedLanHttpOrigin),"old settings have no grant");
   string path=Path.Combine(folder,"lan-policy.json");var library=new StudioLibrary(path);var config=new StudioSettings{Port=FreePort(),Upstream=lan};library.Save(config);
   using(var proxy=new ProxyServer()){
    Reject(delegate{proxy.Start(config);},"untrusted LAN cannot start");Check(!proxy.Running,"failed start leaves listener stopped");
    config.TrustedLanHttpOrigin=origin;library.Save(config);config=new StudioLibrary(path).Data;Check(config.TrustedLanHttpOrigin==origin,"grant persists for exact origin");proxy.Start(config);
    config.Upstream="http://192.168.0.236:1234/v1";Reject(delegate{StudioLibrary.Validate(config);},"stale grant not silently transferred");config.Upstream=lan;
    config.TrustedLanHttpOrigin="";library.Save(config);proxy.Configure(config);Check(!proxy.Running,"revoking grant stops listener");
    config=new StudioLibrary(path).Data;Reject(delegate{proxy.Start(config);},"revocation survives reload");
    library.Recover(true);Check(String.IsNullOrEmpty(library.Data.TrustedLanHttpOrigin),"recovering an old granted backup does not undo revocation");Reject(delegate{proxy.Start(library.Data);},"recovered LAN service requires fresh consent");
    config.Upstream="http://127.0.0.1:1234/v1";proxy.Configure(config);config.Upstream=lan;Reject(delegate{proxy.Start(config);},"returning to prior LAN address does not revive grant");
   }
  }
  // Restarts use a fresh port: Mono/Linux retains closed connections in TIME_WAIT
  // with ExclusiveAddressUse. Windows same-port restart is a separate UI check.
  static void NetworkTests(string body){
   using(var upstream=new Fixture())using(var proxy=new ProxyServer()){
    int port=FreePort();var config=new StudioSettings{Port=port,Upstream="http://127.0.0.1:"+upstream.Port+"/v1",CaptureRequests=true,Entries=new List<LoreEntry>{new LoreEntry{Name="追加",Always=true,Content="测试世界书"}}};proxy.Start(config);
    using(var conflict=new ProxyServer())Reject(delegate{conflict.Start(config);},"Port conflict");
    string response=Call(port,"/v1/chat/completions",body);Check(response.Contains("fixture-ok"),"JSON response");Check(upstream.LastBody.Contains("测试世界书")&&upstream.LastAuthorization=="Bearer fixture-secret","lore injected and auth forwarded");
    Check(Call(port,"/v1/models",null).Contains("fixture"),"models route");Check(Call(port,"/v1/chat/completions",body.Replace("fixture","error")).StartsWith("401:"),"upstream 401 preserved");
    using(var redirectTarget=new Fixture()){
     upstream.RedirectLocation="http://127.0.0.1:"+redirectTarget.Port+"/v1/chat/completions";
     Check(Call(port,"/v1/chat/completions",body.Replace("fixture","redirect")).StartsWith("302:"),"redirect returned without following");
     Check(redirectTarget.RequestCount==0&&redirectTarget.LastAuthorization==null,"redirect target receives neither body nor auth");
    }
    string stream=StreamFirst(port,body.Replace("false","true"));Check(stream.Contains("[DONE]")&&stream.Contains("delta"),"chunked SSE decoded then streamed");
    for(int i=0;i<30&&proxy.Snapshot().Any(r=>r.Status=="转发中");i++)Thread.Sleep(10);
    var records=proxy.Snapshot();Check(records.Count==4&&!Json.Write(records).Contains("fixture-secret"),"No auth in logs");Check(records.All(r=>r.Before!=r.After),"before after captured");proxy.Clear();Check(proxy.Snapshot().Count==0,"clear records");
    config.CaptureRequests=false;proxy.Configure(config);Call(port,"/v1/chat/completions",body);Check(proxy.Snapshot().Count==0,"capture off");
    Check(Call(port,"/v1/chat/completions","{broken").StartsWith("400:"),"Invalid JSON returns 400");
    config.Upstream="http://127.0.0.1:"+FreePort()+"/v1";proxy.Configure(config);Check(!proxy.Running,"upstream change stops listener");config.Port=port=FreePort();proxy.Start(config);Check(Call(port,"/v1/chat/completions",body).StartsWith("502:"),"Unreachable upstream returns 502");
    config.Upstream="http://127.0.0.1:"+upstream.Port+"/v1";config.CaptureRequests=true;proxy.Configure(config);config.Port=port=FreePort();proxy.Start(config);
    string pendingResult=null;var pending=new Thread(delegate(){try{pendingResult=Call(port,"/v1/chat/completions",body.Replace("fixture","hold"));}catch{}}){IsBackground=true};pending.Start();Check(upstream.Held.WaitOne(5000),"pending request entered upstream");proxy.Clear();config.Upstream="http://127.0.0.1:"+upstream.Port+"/changed/v1";proxy.Configure(config);Check(!proxy.Running,"connection change stops in-flight requests");Check(pending.Join(5000),"stopped request terminates before upstream responds");Check(pendingResult==null||!pendingResult.Contains("fixture-ok"),"stopped request cannot complete successfully");upstream.Release.Set();config.Port=port=FreePort();proxy.Start(config);Thread.Sleep(100);Check(proxy.Snapshot().Count==0,"cleared in-flight records never reappear");Check(Call(port,"/v1/chat/completions",body).Contains("fixture-ok"),"restart accepts fresh request");proxy.Stop();Check(!proxy.Running,"stop");
   }
  }
  static string StreamFirst(int port,string body){var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port+"/v1/chat/completions");request.Proxy=null;request.Method="POST";request.Timeout=10000;request.ServicePoint.Expect100Continue=false;byte[] bytes=Encoding.UTF8.GetBytes(body);request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var clock=System.Diagnostics.Stopwatch.StartNew();string first=reader.ReadLine();Check(first.Contains("delta")&&clock.ElapsedMilliseconds<800,"first SSE event available before delayed end");string rest=reader.ReadToEnd();Check(clock.ElapsedMilliseconds>=800,"upstream end intentionally delayed");return first+rest;}}
  internal static string Call(int port,string path,string body){var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port+path);request.Proxy=null;request.Timeout=10000;request.ServicePoint.Expect100Continue=false;request.Headers["Authorization"]="Bearer fixture-secret";
   if(body!=null){request.Method="POST";request.ContentType="application/json";byte[] bytes=Encoding.UTF8.GetBytes(body);request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
   try{using(var response=request.GetResponse())using(var sr=new StreamReader(response.GetResponseStream())){Check(response.Headers["Location"]==null,"upstream redirect location not exposed");return ((int)((HttpWebResponse)response).StatusCode>=300?((int)((HttpWebResponse)response).StatusCode)+":":"")+sr.ReadToEnd();}}catch(WebException ex){if(ex.Response==null)throw;using(var response=(HttpWebResponse)ex.Response)using(var sr=new StreamReader(response.GetResponseStream()))return ((int)response.StatusCode)+":"+sr.ReadToEnd();}
  }
  internal sealed class Fixture : IDisposable {
   TcpListener listener;Thread thread;public int Port;public string LastBody,LastAuthorization,RedirectLocation;public int RequestCount;bool stop;public ManualResetEvent Held=new ManualResetEvent(false),Release=new ManualResetEvent(false);
   public Fixture(){listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;thread=new Thread(Serve){IsBackground=true};thread.Start();}
   void Serve(){while(!stop){TcpClient c;try{c=listener.AcceptTcpClient();}catch{return;}try{using(c)using(var s=c.GetStream()){var header=new List<byte>();while(true){int b=s.ReadByte();if(b<0)throw new IOException();header.Add((byte)b);int n=header.Count;if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;}string h=Encoding.ASCII.GetString(header.ToArray());int length=0;foreach(string line in h.Split('\n')){if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());if(line.StartsWith("Authorization:",StringComparison.OrdinalIgnoreCase))LastAuthorization=line.Substring(14).Trim();}var bytes=new byte[length];for(int i=0;i<length;){int n=s.Read(bytes,i,length-i);if(n==0)throw new IOException();i+=n;}LastBody=Encoding.UTF8.GetString(bytes);Interlocked.Increment(ref RequestCount);
    bool streaming=LastBody.Contains("\"stream\":true"),error=LastBody.Contains("\"error\"");string result=h.StartsWith("GET")?"{\"data\":[{\"id\":\"fixture\"}]}":"{\"choices\":[{\"message\":{\"content\":\"fixture-ok\"}}]}";
    if(LastBody.Contains("\"hold\"")){Held.Set();Release.WaitOne(10000);}
    if(LastBody.Contains("\"redirect\"")){Write(s,"HTTP/1.1 302 Found\r\nLocation: "+RedirectLocation+"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");}
    else if(streaming){Write(s,"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n");foreach(string piece in new[]{"data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n","data: [DONE]\n\n"}){Write(s,Encoding.UTF8.GetByteCount(piece).ToString("x")+"\r\n"+piece+"\r\n");s.Flush();Thread.Sleep(1000);}Write(s,"0\r\n\r\n");}
    else Write(s,"HTTP/1.1 "+(error?"401 Unauthorized":"200 OK")+"\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(result)+"\r\nConnection: close\r\n\r\n"+result);
   }}catch{if(stop)return;}}}
   static void Write(Stream stream,string text){byte[] bytes=Encoding.UTF8.GetBytes(text);stream.Write(bytes,0,bytes.Length);}
   public void Dispose(){stop=true;Release.Set();listener.Stop();thread.Join(3000);}
  }
 }
}
