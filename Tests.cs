using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace WorldApartAI {
 static class Tests {
  static void Assert(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);}
  static void Throws(Action a,string name){bool threw=false;try{a();}catch{threw=true;}Assert(threw,name);}
  public static void Run(string folder,Catalog catalog){
   Assert(catalog.MatchVersion(catalog.MetadataHash)==catalog.Version,"current version accepted");
   Assert(catalog.MatchVersion(new string('0',64))==null&&catalog.MatchVersion(null)==null,"unknown version rejected");
   if(catalog.CompatibleVersions!=null)foreach(var pair in catalog.CompatibleVersions)Assert(catalog.MatchVersion(pair.Key.ToUpperInvariant())==pair.Value,"previous compatible version accepted");
   Directory.CreateDirectory(folder);string root=Path.Combine(folder,"mod-"+Guid.NewGuid().ToString("N"));
   var prefs=new MemoryPrefs{Bytes=Encoding.UTF8.GetBytes("other.mod:tables\0")};var store=new Store(root,catalog,prefs);int id=100000;
   string english=store.Style(id,"en-US",true);store.Seconds=300;store.SetStyle(id,"zh-Hans","验证：说话简洁，并用「客官」称呼对方。\n第二行保留。");
   Throws(delegate{store.Save(true);},"running game blocks writes");Assert(!Directory.Exists(root),"no partial write when running");
   store.Save(false);Assert(store.IsEnabled(),"enabled");Assert(Encoding.UTF8.GetString(prefs.Bytes).Contains("other.mod:tables"),"other preferences preserved");
   var reloaded=new Store(root,catalog,prefs);Assert(reloaded.Seconds==300,"timeout persisted");Assert(reloaded.Style(id,"en-US",false)==english,"other language preserved");Assert(reloaded.Style(id,"zh-Hans",false).Contains("第二行"),"Unicode and newline persisted");
   var patch=Json.Obj(File.ReadAllText(store.PathFor("tables/tbnpcaipersona.patch.json")));var row=Json.Dict(Json.List(patch["upsert"])[0]);Assert(row.Keys.OrderBy(k=>k).SequenceEqual(new[]{"npcId","speakStyle"}),"only target fields patched");
   row["hiddenPersona"]=new Dictionary<string,object>{{"zh-Hans","已有其他字段"}};File.WriteAllText(store.PathFor("tables/tbnpcaipersona.patch.json"),Json.Write(patch));
   Throws(delegate{reloaded.Save(false);},"external edit conflict");reloaded.Reload();reloaded.SetStyle(id,"zh-Hans",reloaded.Style(id,"zh-Hans",true));reloaded.Save(false);
   patch=Json.Obj(File.ReadAllText(store.PathFor("tables/tbnpcaipersona.patch.json")));row=Json.Dict(Json.List(patch["upsert"])[0]);Assert(!row.ContainsKey("speakStyle")&&row.ContainsKey("hiddenPersona"),"restore preserves unrelated fields");
   string before=File.ReadAllText(store.PathFor("tables/tbconstants.patch.json"));byte[] oldPrefs=prefs.Read();reloaded.Seconds=600;prefs.FailNext=true;
   Throws(delegate{reloaded.Save(false);},"registry failure");Assert(File.ReadAllText(store.PathFor("tables/tbconstants.patch.json"))==before,"files rolled back");Assert(prefs.Read().SequenceEqual(oldPrefs),"preferences rolled back");
   reloaded.Seconds=0;Throws(delegate{reloaded.Save(false);},"invalid timeout");reloaded.Seconds=300;
   Throws(delegate{reloaded.SetStyle(id,"zh-Hans",new string('a',8001));},"style length limit");
   reloaded.Disable(false);Assert(!reloaded.IsEnabled()&&Encoding.UTF8.GetString(prefs.Bytes)=="other.mod:tables\0","disable preserves other mod");
   Assert(Directory.GetDirectories(Path.Combine(root,"editor-backups")).Length>=4,"backups created");
   File.WriteAllText(Path.Combine(folder,"PASSED.txt"),"PASS: running game guard; transactional rollback; external edit conflict; Unicode/newlines; language and unrelated field preservation; restore; range checks; backups; enable/disable.\nNPC catalog rows: "+catalog.Npcs.Count+"\nTest mod: "+root,Encoding.UTF8);
  }
 }
}
