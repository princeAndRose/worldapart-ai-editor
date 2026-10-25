using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace WorldApartAI {
 static class FeedbackTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Feedback: "+name);}
  static void Reject(Action a,string name){bool failed=false;try{a();}catch{failed=true;}Check(failed,name);}
  public static void Run(string folder,Catalog catalog){
   if(catalog.CompatibleVersions!=null)foreach(var previous in catalog.CompatibleVersions){
    var oldCatalog=Json.Read<Catalog>(Json.Write(catalog));oldCatalog.MetadataHash=previous.Key;oldCatalog.Version=previous.Value;
    var oldStore=new Store(Path.Combine(folder,"previous-"+Guid.NewGuid().ToString("N")),oldCatalog,new MemoryPrefs());
    string priorKey=Store.NpcKey(110402,"propPlot","zh-Hans");oldStore.Seconds=600;oldStore.SetValue(priorKey,"旧版背景方案");
    var oldPreset=oldStore.ExportPreset();var oldPlan=CharacterPlan.Capture(oldStore,110402,"更新前方案");oldStore.Save(false);
    var upgraded=new Store(oldStore.Root,catalog,new MemoryPrefs());Check(upgraded.Seconds==600&&upgraded.Value(priorKey,false)=="旧版背景方案","saved overrides survive game update");
    upgraded.SetValue(priorKey,"新版草稿");upgraded.ImportPreset(oldPreset);Check(upgraded.Seconds==600&&upgraded.Value(priorKey,false)=="旧版背景方案","previous compatible preset imports");
    upgraded.SetValue(priorKey,"另一份草稿");oldPlan.Apply(upgraded,110402);Check(upgraded.Value(priorKey,false)=="旧版背景方案","previous compatible plan applies");
    oldPreset.MetadataHash=new string('0',64);string snapshot=Json.Write(upgraded.ExportPreset());Reject(delegate{upgraded.ImportPreset(oldPreset);},"unverified version remains rejected");Check(snapshot==Json.Write(upgraded.ExportPreset()),"rejected version leaves draft intact");
   }
   var store=new Store(Path.Combine(folder,"feedback-"+Guid.NewGuid().ToString("N")),catalog,new MemoryPrefs());
   Check(catalog.Npcs.All(n=>n.Id>0),"placeholder IDs excluded from character and prompt routing");
   foreach(int id in new[]{110402,150101,193401,194401}){
    var npc=catalog.Npcs.Single(n=>n.Id==id);Check(npc.BackgroundOnly&&npc.Fields.Keys.SequenceEqual(new[]{"propPlot"}),"missing character exposes only existing background");
    string key=Store.NpcKey(id,"propPlot","zh-Hans");store.SetValue(key,"背景测试 / "+id);
    Reject(delegate{store.SetStyle(id,"zh-Hans","unsupported");},"cannot fabricate persona via style API");
    var plan=CharacterPlan.Capture(store,id,"背景方案");Check(plan.Values.Count==3&&!plan.Values.Any(v=>v.Key.Contains("speakStyle")),"background-only plans contain actual fields");
    store.SetValue(key,"变体");plan.Apply(store,id);Check(store.Value(key,false)=="背景测试 / "+id,"background-only plan roundtrip");
   }
   var preset=store.ExportPreset();var target=new Store(store.Root,catalog,new MemoryPrefs());target.ImportPreset(preset);
   Check(target.Extra.Count==4,"all background preset values imported");
   var invalid=Json.Read<EditorPreset>(Json.Write(preset));invalid.Values.Add(new PresetValue{Key=Store.NpcKey(193401,"speakStyle","zh-Hans"),Value="unsupported"});
   string before=Json.Write(target.ExportPreset());Reject(delegate{target.ImportPreset(invalid);},"unsupported style rejected before any mutation");Check(before==Json.Write(target.ExportPreset()),"invalid mixed import is atomic");
   Check(!store.LastPatchWrite().HasValue&&!store.PatchChangedOnDisk(),"new store diagnostic");store.Save(false);
   Check(store.LastPatchWrite().HasValue&&!store.PatchChangedOnDisk(),"saved diagnostic");
   var baseRows=Json.List(Json.Obj(File.ReadAllText(store.PathFor("tables/tbnpcbasecfg.patch.json")))["upsert"]);
   Check(baseRows.Count==4&&Json.List(Json.Obj(File.ReadAllText(store.PathFor("tables/tbnpcaipersona.patch.json")))["upsert"]).Count==0,"new characters write only base table");
   // A separately authored persona for a background-only directory entry must survive.
   var patch=Store.Patch("tbnpcaipersona","npcId");patch["upsert"]=new[]{new Dictionary<string,object>{{"npcId",193401},{"speakStyle",new Dictionary<string,string>{{"zh-Hans","来自其他工具"}}},{"futureField","保留"}}};
   File.WriteAllText(store.PathFor("tables/tbnpcaipersona.patch.json"),Json.Write(patch));Check(store.PatchChangedOnDisk(),"external changes detected");store.Reload();store.Save(false);
   var preserved=Json.List(Json.Obj(File.ReadAllText(store.PathFor("tables/tbnpcaipersona.patch.json")))["upsert"]).Select(Json.Dict).Single();
   Check(Json.Write(preserved)==Json.Write(((Dictionary<string,object>[])patch["upsert"])[0]),"unmanaged persona preserved byte values");
   var originalPlan=CharacterPlan.Capture(store,100000,"旧版字段方案");originalPlan.Apply(store,100000);
   Check(originalPlan.Values.Count>3,"existing full plan field set retained");
   Check(catalog.Npcs.Single(n=>n.Id==191401).Fields.Keys.All(k=>!k.StartsWith("daoCompanionAttitudeByIntimacy:")),"no invented Chi Yao companion tiers");
   File.WriteAllText(Path.Combine(folder,"FEEDBACK-PASSED.txt"),"PASS: missing base-table characters; background-only field scope, plans and presets; atomic rejection of unsupported style; base-only writes; preserve externally authored persona; diagnostic file/time state; existing plan; no invented relationship tiers.");
  }
 }
}
