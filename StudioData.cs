using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace WorldApartAI {
 public sealed class CharacterPlan {
  public string Id,Name,MetadataHash; public int NpcId; public List<PresetValue> Values;
  public override string ToString(){return Name;}
  public static CharacterPlan Capture(Store store,int id,string name){
   if(String.IsNullOrWhiteSpace(name)||name.Length>80)throw new InvalidDataException("方案名称需要 1–80 个字符。");
   var npc=store.Catalog.Npcs.First(n=>n.Id==id);
   var values=store.Fields.Values.Where(f=>f.NpcId==id).Select(f=>new PresetValue{Key=f.Key,Value=store.Value(f.Key,false)}).ToList();
   foreach(string lang in new[]{"zh-Hans","zh-Hant","en-US"})values.Add(new PresetValue{Key=Store.NpcKey(id,"speakStyle",lang),Value=store.Style(id,lang,false)});
   return new CharacterPlan{Id=Guid.NewGuid().ToString("N"),Name=name.Trim(),NpcId=npc.Id,MetadataHash=store.Catalog.MetadataHash,Values=values};
  }
  public void Apply(Store store,int id){
   if(id!=NpcId||Values==null||Values.Any(v=>v==null||v.Key==null||!v.Key.StartsWith("npc/"+id+"/",StringComparison.Ordinal)))throw new InvalidDataException("此方案属于另一个人物，未修改草稿。");
   var expected=Capture(store,id,"检查").Values.Select(v=>v.Key).OrderBy(k=>k).ToArray();
   if(!Values.Select(v=>v.Key).OrderBy(k=>k).SequenceEqual(expected))throw new InvalidDataException("方案字段与当前人物不匹配，请保留旧方案并重新建立。");
   store.ImportPreset(new EditorPreset{Format="WorldApart.AI.Editor.Preset",Version=1,MetadataHash=MetadataHash,TimeoutSeconds=store.Seconds,Values=Values});
  }
 }
 public sealed class LoreEntry {
  public string Id=Guid.NewGuid().ToString("N"),Name="新条目",Keywords="",SystemMatch="",Content="",Scope="last-user";
  public bool Enabled=true,Always,MatchAll; public int Priority=0;
  public override string ToString(){return (Enabled?"":"[停用] ")+Name;}
 }
 public sealed class StudioSettings {
  public int Version=1,Port=18181,TimeoutSeconds=300,Budget=6000;
  public string Upstream="http://127.0.0.1:1234/v1";
  public bool CaptureRequests=false;
  public List<CharacterPlan> Plans=new List<CharacterPlan>(); public List<LoreEntry> Entries=new List<LoreEntry>();
 }
 public sealed class StudioLibrary {
  public readonly string PathName; string fingerprint; public StudioSettings Data;public string LoadError;
  public StudioLibrary(string path){PathName=path;try{Reload();}catch{LoadError="工作台资料库无法读取。原文件已保留，可恢复备份或重建资料库；人物和超时补丁仍可编辑。";Data=new StudioSettings();}}
  public void Reload(){fingerprint=File.Exists(PathName)?Store.FileHash(PathName):"absent";Data=File.Exists(PathName)?Json.Read<StudioSettings>(File.ReadAllText(PathName,Encoding.UTF8)):new StudioSettings();Validate(Data);}
  public static void Validate(StudioSettings data){
   if(data==null||data.Version!=1||data.Plans==null||data.Entries==null||data.Plans.Count>2000||data.Entries.Count>300)throw new InvalidDataException("编辑器资料库格式不兼容。");
   if(data.Port<1024||data.Port>65535||data.TimeoutSeconds<30||data.TimeoutSeconds>1800||data.Budget<0||data.Budget>24000)throw new InvalidDataException("端口需为 1024–65535，等待时间为 30–1800 秒，世界书预算为 0–24000 字符。");
   ProxyServer.ValidateUpstream(data.Upstream,data.Port);
   if(data.Plans.Any(p=>p==null||String.IsNullOrWhiteSpace(p.Id)||String.IsNullOrWhiteSpace(p.Name)||p.Name.Length>80||String.IsNullOrEmpty(p.MetadataHash)||p.MetadataHash.Length!=64||p.Values==null||p.Values.Count>1000)||data.Plans.Select(p=>p.Id).Distinct().Count()!=data.Plans.Count)throw new InvalidDataException("人物方案结构不合法。");
   foreach(var plan in data.Plans)if(plan.Values.Any(v=>v==null||v.Key==null||v.Value==null||v.Value.Length>24000||!v.Key.StartsWith("npc/"+plan.NpcId+"/",StringComparison.Ordinal))||plan.Values.Select(v=>v.Key).Distinct().Count()!=plan.Values.Count)throw new InvalidDataException("人物方案字段不合法。");
   if(data.Entries.Any(e=>e==null||String.IsNullOrWhiteSpace(e.Id))||data.Entries.Select(e=>e.Id).Distinct().Count()!=data.Entries.Count)throw new InvalidDataException("世界书条目 ID 重复或缺失。");
   foreach(var e in data.Entries){
    if(String.IsNullOrWhiteSpace(e.Name)||e.Name.Length>80||e.Content==null||e.Content.Length>12000||e.Keywords==null||e.Keywords.Length>2000||e.SystemMatch==null||e.SystemMatch.Length>200||Math.Abs((long)e.Priority)>10000||!(e.Scope=="last-user"||e.Scope=="recent-users"))throw new InvalidDataException("世界书条目内容或条件不合法。");
    if(e.Enabled&&(!e.Always&&Tokens(e.Keywords).Length==0))throw new InvalidDataException("启用的条目需填写关键词，或选择始终启用。");
    if(e.Enabled&&String.IsNullOrWhiteSpace(e.Content))throw new InvalidDataException("启用的条目需填写提示词正文。");
   }
  }
  public static string[] Tokens(string text){return (text??"").Split(new[]{',','，','\n','\r'},StringSplitOptions.RemoveEmptyEntries).Select(t=>t.Trim()).Where(t=>t.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
  public void Save(StudioSettings next){
   if(LoadError!=null)throw new IOException(LoadError);
   Validate(next);if((File.Exists(PathName)?Store.FileHash(PathName):"absent")!=fingerprint)throw new IOException("另一份编辑器修改了资料库。请关闭后重新打开；本次未覆盖。");
   Directory.CreateDirectory(Path.GetDirectoryName(PathName));string temp=PathName+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(temp,Json.Write(next),new UTF8Encoding(false));if(File.Exists(PathName))File.Replace(temp,PathName,PathName+".bak");else File.Move(temp,PathName);}finally{if(File.Exists(temp))File.Delete(temp);}
   Data=Json.Read<StudioSettings>(Json.Write(next));fingerprint=Store.FileHash(PathName);
  }
  public void Recover(bool fromBackup){
   var next=fromBackup?Json.Read<StudioSettings>(File.ReadAllText(PathName+".bak",Encoding.UTF8)):new StudioSettings();Validate(next);
   if(File.Exists(PathName)){if(Store.FileHash(PathName)!=fingerprint)throw new IOException("资料库再次变化，请重新打开编辑器。");File.Copy(PathName,PathName+".preserved-"+Guid.NewGuid().ToString("N")+".json");}
   string old=LoadError;LoadError=null;try{Save(next);}catch{LoadError=old;throw;}
  }
 }
 public sealed class PromptTrace {
  public string Before,After,Injected="";public List<string> Decisions=new List<string>();public int AddedCharacters;
  public static string ContentText(Dictionary<string,object> message){object value;if(!message.TryGetValue("content",out value)||value==null)return "";if(value is string)return (string)value;
   try{return String.Join("\n",Json.List(value).Select(o=>Json.Dict(o)).Where(o=>o.ContainsKey("type")&&Convert.ToString(o["type"])=="text"&&o.ContainsKey("text")).Select(o=>Convert.ToString(o["text"])));}catch{return "";}
  }
  public static PromptTrace Build(string body,StudioSettings config){try{return BuildCore(body,config);}catch(InvalidDataException){throw;}catch(Exception ex){if(ex is ArgumentException||ex is InvalidCastException||ex is NullReferenceException)throw new InvalidDataException("请求 JSON 或 messages 结构无效。");throw;}}
  static PromptTrace BuildCore(string body,StudioSettings config){
   var result=new PromptTrace{Before=body,After=body};var request=Json.Obj(body);object raw;
   if(request==null||!request.TryGetValue("messages",out raw))throw new InvalidDataException("需要包含 messages 的 Chat Completions JSON。");
   var messages=Json.List(raw);if(messages.Count>2000)throw new InvalidDataException("消息数量超过 2000。");
   var mapped=messages.Select(Json.Dict).ToList();if(mapped.Any(m=>!m.ContainsKey("role")))throw new InvalidDataException("消息缺少 role。");
   var users=mapped.Where(m=>Convert.ToString(m["role"])=="user").Select(ContentText).ToList();
   string systems=String.Join("\n",mapped.Where(m=>new[]{"system","developer"}.Contains(Convert.ToString(m["role"]))).Select(ContentText));
   var blocks=new List<string>();foreach(var e in config.Entries.OrderByDescending(e=>e.Priority).ThenBy(e=>e.Id,StringComparer.Ordinal)){
    string reason=null;var keys=StudioLibrary.Tokens(e.Keywords);string search=e.Scope=="recent-users"?String.Join("\n",users.Skip(Math.Max(0,users.Count-4))):users.LastOrDefault()??"";
    if(!e.Enabled)reason="已停用";
    else if(e.SystemMatch.Length>0&&systems.IndexOf(e.SystemMatch,StringComparison.OrdinalIgnoreCase)<0)reason="系统文本条件未命中";
    else if(!e.Always&&(keys.Length==0||!(e.MatchAll?keys.All(k=>search.IndexOf(k,StringComparison.OrdinalIgnoreCase)>=0):keys.Any(k=>search.IndexOf(k,StringComparison.OrdinalIgnoreCase)>=0))))reason="关键词未命中";
    string block="[世界书："+e.Name+"]\n"+e.Content;
    int cost=block.Length+(blocks.Count>0?2:0);
    if(reason==null&&result.AddedCharacters+cost>config.Budget)reason="超出字符预算";
    if(reason==null){blocks.Add(block);result.AddedCharacters+=cost;reason="已插入";}
    result.Decisions.Add(e.Name+" · "+reason);
   }
   result.Injected=String.Join("\n\n",blocks);if(blocks.Count>0){int index=messages.Count;while(index>0&&new[]{"user","tool"}.Contains(Convert.ToString(Json.Dict(messages[index-1])["role"])))index--;
    messages.Insert(index,new Dictionary<string,object>{{"role","system"},{"content",String.Join("\n\n",blocks)}});request["messages"]=messages;result.After=Json.Write(request);
   }return result;
  }
  public static string Readable(string body){
   try{var obj=Json.Obj(body);var output=new StringBuilder();foreach(var p in obj.Where(p=>p.Key!="messages"))output.AppendLine(p.Key+": "+Json.Write(p.Value));int i=0;
    foreach(var m in Json.List(obj["messages"]).Select(Json.Dict)){output.AppendLine("\n━━ "+(++i)+" · "+Convert.ToString(m["role"])+" ━━");output.AppendLine(ContentText(m));foreach(var p in m.Where(p=>p.Key!="content"&&p.Key!="role"))output.AppendLine(p.Key+": "+Json.Write(p.Value));}return output.ToString();
   }catch{return body;}
  }
 }
}
