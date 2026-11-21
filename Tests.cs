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
   var migrationOnly=new Catalog{MetadataHash=catalog.MetadataHash,Version=catalog.Version,ImportVersions=new Dictionary<string,string>{{new string('0',64),"old fixture"}}};
   Assert(migrationOnly.CanImport(new string('0',64))&&migrationOnly.MatchVersion(new string('0',64))==null&&!migrationOnly.CanImport(null),"preset migration permission does not mark old game compatible");
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
   PackagePrefsTests(folder,catalog);
   File.WriteAllText(Path.Combine(folder,"PASSED.txt"),"PASS: running game guard; transactional rollback; external edit conflict; Unicode/newlines; language and unrelated field preservation; restore; range checks; backups; enable/disable.\nNPC catalog rows: "+catalog.Npcs.Count+"\nTest mod: "+root,Encoding.UTF8);
  }
  static void PackagePrefsTests(string folder,Catalog catalog){
   var encoding=new UTF8Encoding(false,true);
   var prefs=new MemoryPrefs{Bytes=encoding.GetBytes("other.mod:tables\nlocal.ai-timeout:tables\0"),DisabledPackages=encoding.GetBytes("other.disabled\r\n local.ai-timeout \r\nlocal.ai-timeout\nother.tail\0")};
   var store=new Store(Path.Combine(folder,"package-"+Guid.NewGuid().ToString("N")),catalog,prefs);
   Assert(!store.IsEnabled()&&store.IsPackageDisabled(),"package disable overrides enabled table entry with game whitespace parsing");
   byte[] originalEntries=prefs.Read(),originalDisabled=prefs.ReadDisabledPackages();
   prefs.FailNextDisabled=true;
   Throws(delegate{store.Save(false);},"second preference write fails");
   Assert(prefs.Read().SequenceEqual(originalEntries)&&prefs.ReadDisabledPackages().SequenceEqual(originalDisabled),"both preference values roll back on second write failure");
   Assert(!File.Exists(store.PathFor("mod.json")),"new files removed after package failure");
   var backup=Json.Obj(File.ReadAllText(Path.Combine(store.LastBackup,"transaction.json")));
   Assert((bool)backup["DisabledPackagesExisted"]&&Convert.FromBase64String((string)backup["DisabledPackagesBase64"]).SequenceEqual(originalDisabled),"backup records exact disabled packages bytes");
   prefs.FailDisabledAfterWrite=true;
   Throws(delegate{store.Save(false);},"package verification fails after mutation");
   Assert(prefs.Read().SequenceEqual(originalEntries)&&prefs.ReadDisabledPackages().SequenceEqual(originalDisabled),"rollback restores package value after mutation");
   store.Save(false);
   Assert(store.IsEnabled()&&!store.IsPackageDisabled(),"save removes all matching package blocks");
   Assert(encoding.GetString(prefs.ReadDisabledPackages())=="other.disabled\r\nother.tail\0","other disabled package bytes preserved");
   byte[] enabled=prefs.Read(),remaining=prefs.ReadDisabledPackages();
   var existingFiles=store.Build().Keys.ToDictionary(f=>f,f=>File.ReadAllBytes(store.PathFor(f)));
   prefs.DisabledPackages=originalDisabled.ToArray();store.Seconds=600;prefs.FailDisabledAfterWrite=true;
   Throws(delegate{store.Save(false);},"package failure while updating existing files");
   Assert(existingFiles.All(p=>File.ReadAllBytes(store.PathFor(p.Key)).SequenceEqual(p.Value)),"all existing patch files restored after second-key failure");
   Assert(prefs.Read().SequenceEqual(enabled)&&prefs.ReadDisabledPackages().SequenceEqual(originalDisabled),"existing preferences restored after second-key mutation");
   prefs.DisabledPackages=remaining;store.Reload();
   prefs.FailNext=true;Throws(delegate{store.Disable(false);},"disable entry write fails");
   Assert(store.IsEnabled()&&prefs.Read().SequenceEqual(enabled)&&prefs.ReadDisabledPackages().SequenceEqual(remaining),"failed disable preserves both states");
   store.Disable(false);Assert(!store.IsEnabled()&&prefs.ReadDisabledPackages().SequenceEqual(remaining),"disable preserves package block list");
   var legacy=new MemoryPrefs();var legacyStore=new Store(Path.Combine(folder,"legacy-"+Guid.NewGuid().ToString("N")),catalog,legacy);
   legacyStore.Save(false);Assert(legacyStore.IsEnabled()&&legacy.ReadDisabledPackages()==null,"missing package key remains absent on save");
   backup=Json.Obj(File.ReadAllText(Path.Combine(legacyStore.LastBackup,"transaction.json")));Assert(!(bool)backup["DisabledPackagesExisted"]&&backup["DisabledPackagesBase64"]==null,"backup preserves absence of package key");
   legacyStore.Disable(false);Assert(!legacyStore.IsEnabled()&&legacy.ReadDisabledPackages()==null,"old version disable does not create package key");
   var invalid=new MemoryPrefs{DisabledPackages=new byte[]{0xff}};var invalidStore=new Store(Path.Combine(folder,"invalid-"+Guid.NewGuid().ToString("N")),catalog,invalid);
   Throws(delegate{invalidStore.Save(false);},"invalid package encoding rejected before writes");Assert(!Directory.Exists(invalidStore.Root),"invalid preferences produce no files");
  }
 }
}
