using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace WorldApartAI
{
 public sealed class Catalog {
  public string MetadataHash; public string Version; public List<Npc> Npcs;
  public Dictionary<string,string> CompatibleVersions;
  public List<PromptTemplate> Prompts;
  public string MatchVersion(string hash) {
   if(String.IsNullOrEmpty(hash))return null;
   if(String.Equals(hash,MetadataHash,StringComparison.OrdinalIgnoreCase))return Version;
   if(CompatibleVersions!=null)foreach(var pair in CompatibleVersions)
    if(String.Equals(hash,pair.Key,StringComparison.OrdinalIgnoreCase))return pair.Value;
   return null;
  }
 }
 public sealed class Npc {
  public int Id; public string Name; public string Title; public Dictionary<string,string> Style; public string Portrait;
  public Dictionary<string,Dictionary<string,string>> Fields;
  public override string ToString() { return Name+"  ·  "+Id+(String.IsNullOrWhiteSpace(Title)?"":"  /  "+Title); }
 }
 public static class Json {
  public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=16000000, RecursionLimit=120 }; }
  public static string Write(object o) { return Serializer().Serialize(o); }
  public static T Read<T>(string s) { return Serializer().Deserialize<T>(s); }
  public static Dictionary<string,object> Obj(string s) { return Read<Dictionary<string,object>>(s); }
  public static Dictionary<string,object> Dict(object o) { return (Dictionary<string,object>)o; }
  public static List<object> List(object o) { return ((IEnumerable)o).Cast<object>().ToList(); }
  public static Dictionary<string,object> Copy(Dictionary<string,object> o) { return Obj(Write(o)); }
 }
 public interface IPrefs { byte[] Read(); void Write(byte[] bytes); }
 public sealed class RealPrefs : IPrefs {
  const string Key="Software\\Nuverse\\WorldApart", Value="MOD_ENABLED_ENTRIES_V2_h2913630511";
  public byte[] Read() {
   using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))
   using(var key=root.OpenSubKey(Key)) {
    if(key==null) throw new InvalidOperationException("未找到游戏设置。请先正常启动并退出一次游戏。");
    object value=key.GetValue(Value); if(value==null)return null;
    if(key.GetValueKind(Value)!=RegistryValueKind.Binary)throw new InvalidDataException("游戏 Mod 启用项格式不兼容，未作更改。");
    return (byte[])value;
   }
  }
  public void Write(byte[] bytes) {
   using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))
   using(var key=root.OpenSubKey(Key,true)) {
    if(key==null)throw new InvalidOperationException("游戏设置不可写。");
    if(bytes==null)key.DeleteValue(Value,false);else key.SetValue(Value,bytes,RegistryValueKind.Binary);
    key.Flush();
   }
   byte[] read=Read(); if(bytes==null?read!=null:read==null||!read.SequenceEqual(bytes))throw new IOException("启用记录写入校验失败。");
  }
 }
 public sealed class MemoryPrefs : IPrefs {
  public byte[] Bytes; public bool FailNext;
  public byte[] Read() { return Bytes==null?null:Bytes.ToArray(); }
  public void Write(byte[] b) { if(FailNext){FailNext=false;throw new IOException("Simulated registry failure");}Bytes=b==null?null:b.ToArray(); }
 }
 public sealed partial class Store {
  public const string ModId="local.ai-timeout", Entry="local.ai-timeout:tables";
  public string Root; public Catalog Catalog; public int Seconds=180;
  public Dictionary<int,Dictionary<string,string>> Styles=new Dictionary<int,Dictionary<string,string>>();
  public string LastBackup; public IPrefs Prefs;
  Dictionary<string,object> constants,persona,manifest;
  Dictionary<string,string> fingerprints=new Dictionary<string,string>();
  Dictionary<int,Npc> npcs;
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  static readonly string[] Files={"mod.json","tables/tbconstants.patch.json","tables/tbnpcaipersona.patch.json","tables/tbnpcbasecfg.patch.json","tables/tbaiprompttemplate.patch.json"};
  public Store(string root,Catalog catalog,IPrefs prefs){Root=Path.GetFullPath(root);Catalog=catalog;Prefs=prefs;npcs=catalog.Npcs.ToDictionary(n=>n.Id);Reload();}
  public static Dictionary<string,object> Patch(string table,string key){return new Dictionary<string,object>{{"table",table},{"keyFields",new object[]{key}},{"upsert",new object[0]}};}
  public string PathFor(string rel){return Path.Combine(Root,rel.Replace('/',Path.DirectorySeparatorChar));}
  public static string FileHash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  string Fingerprint(string rel){return File.Exists(PathFor(rel))?FileHash(PathFor(rel)):"absent";}
  Dictionary<string,object> Load(string file,Dictionary<string,object> fallback){return File.Exists(PathFor(file))?Json.Obj(File.ReadAllText(PathFor(file),Utf8)):fallback;}
  static void ValidatePatch(Dictionary<string,object> patch,string table,string key){
   if(!patch.ContainsKey("table")||Convert.ToString(patch["table"])!=table||!patch.ContainsKey("keyFields")||
    !Json.List(patch["keyFields"]).Select(Convert.ToString).SequenceEqual(new[]{key})||!patch.ContainsKey("upsert"))
    throw new InvalidDataException("已有补丁结构不兼容："+table+"。未覆盖文件。");
   var ids=new HashSet<int>();
   foreach(object item in Json.List(patch["upsert"])){var r=Json.Dict(item);if(!r.ContainsKey(key)||!ids.Add(Convert.ToInt32(r[key])))throw new InvalidDataException("补丁定位字段缺失或重复："+table);}
  }
  public void Reload(){
   manifest=Load(Files[0],new Dictionary<string,object>{{"schemaVersion",5},{"modId",ModId},{"title","AI 编辑器设置"},{"author","Local"},{"version","0.4.0"},{"priority",0},{"units",new object[0]}});
   if(Convert.ToString(manifest["modId"])!=ModId||Convert.ToInt32(manifest["schemaVersion"])!=5)throw new InvalidDataException("已有 Mod 清单不兼容。");
   constants=Load(Files[1],Patch("tbconstants","initialSpaceId"));persona=Load(Files[2],Patch("tbnpcaipersona","npcId"));
   ValidatePatch(constants,"tbconstants","initialSpaceId");ValidatePatch(persona,"tbnpcaipersona","npcId");
   Seconds=180;
   foreach(var r in Json.List(constants["upsert"]).Select(Json.Dict))if(Convert.ToInt32(r["initialSpaceId"])==100&&r.ContainsKey("CHAT_API_TIMEOUT_SECONDS"))Seconds=Convert.ToInt32(r["CHAT_API_TIMEOUT_SECONDS"]);
   if(Seconds<30||Seconds>1800)throw new InvalidDataException("已有超时值不在支持范围 30—1800 秒内。");
   Styles.Clear();
   foreach(var r in Json.List(persona["upsert"]).Select(Json.Dict)){
    int id=Convert.ToInt32(r["npcId"]);
    if(r.ContainsKey("speakStyle")&&npcs.ContainsKey(id)){
     var style=new Dictionary<string,string>(npcs[id].Style);
     foreach(var pair in Json.Dict(r["speakStyle"]))style[pair.Key]=Convert.ToString(pair.Value);
     Styles[id]=style;
    }
   }
   ReloadExtra();foreach(string file in Files)fingerprints[file]=Fingerprint(file);
  }
  public string Style(int id,string lang,bool original){
   var values=!original&&Styles.ContainsKey(id)?Styles[id]:npcs[id].Style;string s;return values.TryGetValue(lang,out s)?s:"";
  }
  public void SetStyle(int id,string lang,string value){
   if(!npcs.ContainsKey(id)||!new[]{"zh-Hans","zh-Hant","en-US"}.Contains(lang))throw new InvalidDataException("NPC 或语言不支持。");
   if(value.Length>8000)throw new InvalidDataException("说话风格最多 8000 字符。");
   if(!Styles.ContainsKey(id))Styles[id]=new Dictionary<string,string>(npcs[id].Style);Styles[id][lang]=value;
   if(Styles[id].Count==npcs[id].Style.Count&&Styles[id].All(kv=>npcs[id].Style.ContainsKey(kv.Key)&&npcs[id].Style[kv.Key]==kv.Value))Styles.Remove(id);
  }
  public bool IsEnabled(){return Entries(Prefs.Read()).Contains(Entry);}
  static List<string> Entries(byte[] b){return b==null?new List<string>():Utf8.GetString(b).TrimEnd('\0').Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.TrimEnd('\r')).ToList();}
  static byte[] EnableBytes(byte[] old,bool enabled){
   var entries=Entries(old);entries.RemoveAll(s=>s==Entry);if(enabled)entries.Add(Entry);return Utf8.GetBytes(String.Join("\n",entries)+"\0");
  }
  public Dictionary<string,byte[]> Build(){
   if(Seconds<30||Seconds>1800)throw new InvalidDataException("超时必须在 30—1800 秒之间。");
   var c=Json.Copy(constants);var rows=Json.List(c["upsert"]);var cr=rows.Select(Json.Dict).FirstOrDefault(r=>Convert.ToInt32(r["initialSpaceId"])==100);
   if(cr==null){cr=new Dictionary<string,object>{{"initialSpaceId",100}};rows.Add(cr);}cr["CHAT_API_TIMEOUT_SECONDS"]=Seconds;c["upsert"]=rows;
   var p=Json.Copy(persona);var prows=Json.List(p["upsert"]);
   foreach(var r in prows.Select(Json.Dict))if(npcs.ContainsKey(Convert.ToInt32(r["npcId"])))r.Remove("speakStyle");
   prows.RemoveAll(o=>Json.Dict(o).Count==1&&npcs.ContainsKey(Convert.ToInt32(Json.Dict(o)["npcId"])));
   foreach(var pair in Styles.OrderBy(k=>k.Key)){
    if(pair.Value.Values.Any(s=>s==null||s.Length>8000))throw new InvalidDataException("说话风格长度不合法。");
    var pr=prows.Select(Json.Dict).FirstOrDefault(r=>Convert.ToInt32(r["npcId"])==pair.Key);
    if(pr==null){pr=new Dictionary<string,object>{{"npcId",pair.Key}};prows.Add(pr);}pr["speakStyle"]=pair.Value;
   }
   p["upsert"]=prows;var m=Json.Copy(manifest);m["title"]="AI 编辑器设置（超时 "+Seconds+" 秒）";m["version"]="0.4.0";
   var output=new Dictionary<string,byte[]>{{Files[0],Utf8.GetBytes(Json.Write(m))},{Files[1],Utf8.GetBytes(Json.Write(c))}};
   BuildExtra(p,output);return output;
  }
  static void CheckClosed(bool gameRunning){if(gameRunning)throw new InvalidOperationException("请先完整退出《不问凡尘》，再保存或禁用。当前草稿已保留。");}
  void CheckConflict(){foreach(string f in Files)if(Fingerprint(f)!=fingerprints[f])throw new IOException("其他工具已修改补丁。请先保留草稿文本，然后关闭并重新打开编辑器。文件未被覆盖。");}
  string Backup(Dictionary<string,byte[]> originals,byte[] prefs){
   string folder=PathFor("editor-backups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(folder);
   foreach(var pair in originals)if(pair.Value!=null){string path=Path.Combine(folder,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pair.Value);}
   File.WriteAllText(Path.Combine(folder,"transaction.json"),Json.Write(new{PrefsExisted=prefs!=null,PrefsBase64=prefs==null?null:Convert.ToBase64String(prefs),FilesExisted=originals.ToDictionary(k=>k.Key,v=>v.Value!=null)}),Utf8);return folder;
  }
  static void AtomicWrite(string path,byte[] bytes){
   Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
  }
  public void Save(bool gameRunning){
   CheckClosed(gameRunning);CheckConflict();var output=Build();var original=Files.ToDictionary(f=>f,f=>File.Exists(PathFor(f))?File.ReadAllBytes(PathFor(f)):null);
   byte[] prefs=Prefs.Read();LastBackup=Backup(original,prefs);var touched=new List<string>();bool prefsAttempted=false;
   try{
    foreach(var file in output){AtomicWrite(PathFor(file.Key),file.Value);touched.Add(file.Key);}
    prefsAttempted=true;Prefs.Write(EnableBytes(prefs,true));if(!IsEnabled())throw new IOException("无法确认 Mod 启用记录。");
   }catch(Exception error){
    var failures=new List<string>();
    foreach(var file in touched)try{if(original[file]==null)File.Delete(PathFor(file));else AtomicWrite(PathFor(file),original[file]);}catch(Exception ex){failures.Add(ex.Message);}
    if(prefsAttempted)try{Prefs.Write(prefs);}catch(Exception ex){failures.Add(ex.Message);}
    throw new IOException(error.Message+(failures.Count==0?"\n已回滚本次更改。":"\n回滚未完全成功，请使用备份恢复："+LastBackup+"\n"+String.Join("\n",failures)),error);
   }
   Reload();
  }
  public void Disable(bool gameRunning){
   CheckClosed(gameRunning);byte[] prefs=Prefs.Read();LastBackup=Backup(new Dictionary<string,byte[]>(),prefs);
   try{Prefs.Write(EnableBytes(prefs,false));if(IsEnabled())throw new IOException("禁用校验失败。");}catch{Prefs.Write(prefs);throw;}
  }
 }
}

