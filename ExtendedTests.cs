using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
namespace WorldApartAI {
 static class ExtendedTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Extended: "+name);}
  static void Reject(Action a,string name){bool failed=false;try{a();}catch{failed=true;}Check(failed,name);}
  public static void Run(string folder,Catalog c){
   var prefs=new MemoryPrefs();var s=new Store(Path.Combine(folder,"extended-"+Guid.NewGuid().ToString("N")),c,prefs);
   string background=Store.NpcKey(100000,"propPlot","zh-Hans"),hidden=Store.NpcKey(100000,"hiddenPersona","zh-Hans"),attitude=Store.NpcKey(100000,"attitudeByIntimacy:4","zh-Hans"),prompt=Store.PromptKey("task.chat","zh-Hans"),flag=Store.NpcKey(190000,"usePersonalIntimacyAttitude","value");
   var d=new DraftState(s);s.SetValue(background,"试验背景");s.SetValue(hidden,"一段隐藏的人设");s.SetValue(attitude,"关系阶段四的态度");s.SetValue(prompt,s.Value(prompt,true)+"\n回应简短。");
   Check(d.Changes().Count==4&&d.Changes().All(x=>x.FieldKey!=null),"field identity in draft");s.Save(false);d.Accept();Check(d.Changes().Count==0,"extended accept");
   var reloaded=new Store(s.Root,c,prefs);Check(reloaded.Value(background,false)=="试验背景"&&reloaded.Value(attitude,false)=="关系阶段四的态度","new table and relationship persistence");Check(reloaded.Value(flag,false)=="false","no automatic relation enabling");
   var patch=Json.Obj(File.ReadAllText(s.PathFor("tables/tbnpcaipersona.patch.json")));var row=Json.List(patch["upsert"]).Select(Json.Dict).First(r=>Convert.ToInt32(r["npcId"])==100000);
   var pairs=Json.List(row["attitudeByIntimacy"]).Select(Json.List).ToList();Check(pairs.Select(p=>Convert.ToInt32(p[0])).SequenceEqual(new[]{0,1,2,3,4,5,-1}),"original tier order and complete array");
   Check(Convert.ToString(Json.Dict(pairs[4][1])["en-US"])==c.Npcs.First(n=>n.Id==100000).Fields["attitudeByIntimacy:4"]["en-US"],"other relationship language preserved");
   row["futureField"]="keep";Json.Dict(row["hiddenPersona"])["ja-JP"]="未知语言";var unknown=new object[]{99,new Dictionary<string,object>{{"zh-Hans","未知阶段"}}};var pp=Json.List(row["attitudeByIntimacy"]);pp.Add(unknown);row["attitudeByIntimacy"]=pp;File.WriteAllText(s.PathFor("tables/tbnpcaipersona.patch.json"),Json.Write(patch));
   Reject(delegate{reloaded.Save(false);},"extended external conflict");reloaded.Reload();reloaded.SetValue(hidden,reloaded.Value(hidden,true));reloaded.Save(false);
   patch=Json.Obj(File.ReadAllText(s.PathFor("tables/tbnpcaipersona.patch.json")));row=Json.List(patch["upsert"]).Select(Json.Dict).First(r=>Convert.ToInt32(r["npcId"])==100000);Check(Convert.ToString(row["futureField"])=="keep"&&Convert.ToString(Json.Dict(row["hiddenPersona"])["ja-JP"])=="未知语言","unknown field and language preserved");Check(Json.List(row["attitudeByIntimacy"]).Select(Json.List).Any(p=>Convert.ToInt32(p[0])==99),"unknown relationship preserved");
   // An unknown language must not erase the known languages in a replacement array.
   var four=Json.List(row["attitudeByIntimacy"]).Select(Json.List).First(p=>Convert.ToInt32(p[0])==4);Json.Dict(four[1])["ja-JP"]="未知关系语言";File.WriteAllText(s.PathFor("tables/tbnpcaipersona.patch.json"),Json.Write(patch));
   reloaded.Reload();reloaded.SetValue(attitude,reloaded.Value(attitude,true));reloaded.Save(false);
   patch=Json.Obj(File.ReadAllText(s.PathFor("tables/tbnpcaipersona.patch.json")));row=Json.List(patch["upsert"]).Select(Json.Dict).First(r=>Convert.ToInt32(r["npcId"])==100000);four=Json.List(row["attitudeByIntimacy"]).Select(Json.List).First(p=>Convert.ToInt32(p[0])==4);
   Check(Convert.ToString(Json.Dict(four[1])["ja-JP"])=="未知关系语言"&&c.Npcs.First(n=>n.Id==100000).Fields["attitudeByIntimacy:4"].All(kv=>Convert.ToString(Json.Dict(four[1])[kv.Key])==kv.Value),"restore relation preserves all original and unknown languages");
   // Preview must start from the exact current draft, including removed overrides.
   reloaded.SetValue(hidden,"已保存人设");reloaded.SetStyle(100000,"zh-Hans","已保存风格");reloaded.Save(false);var savedPreset=reloaded.ExportPreset();
   reloaded.SetValue(hidden,reloaded.Value(hidden,true));reloaded.SetStyle(100000,"zh-Hans",reloaded.Style(100000,"zh-Hans",true));
   var preview=new Store(reloaded.Root,c,new MemoryPrefs());reloaded.CopyDraftTo(preview);var previewDraft=new DraftState(preview);preview.ImportPreset(savedPreset);
   Check(previewDraft.Changes().Any(x=>x.FieldKey==hidden)&&previewDraft.Changes().Any(x=>x.NpcId==100000&&x.FieldKey==null),"import preview includes draft restores");
   Check(reloaded.Value(hidden,false)==reloaded.Value(hidden,true)&&reloaded.Style(100000,"zh-Hans",false)==reloaded.Style(100000,"zh-Hans",true),"preview does not mutate source draft");
   reloaded.SetValue(prompt,"missing variables");Reject(delegate{reloaded.Save(false);},"missing variables block save");reloaded.SetValue(prompt,reloaded.Value(prompt,true));reloaded.SetStyle(100000,"zh-Hans","预设风格");
   var preset=reloaded.ExportPreset();var target=new Store(Path.Combine(folder,"preset-"+Guid.NewGuid().ToString("N")),c,new MemoryPrefs());target.ImportPreset(preset);Check(!Directory.Exists(target.Root)&&target.Value(background,false)=="试验背景"&&target.Style(100000,"zh-Hans",false)=="预设风格","preset import is draft only");
   string before=Json.Write(target.ExportPreset());preset.Values.Add(new PresetValue{Key="prompt/task.chat/zh-Hans",Value="invalid"});Reject(delegate{target.ImportPreset(preset);},"invalid import rejected");Check(before==Json.Write(target.ExportPreset()),"import rollback no partial draft mutation");
   s.Reload();string oldbase=File.ReadAllText(s.PathFor("tables/tbnpcbasecfg.patch.json")),oldprompt=File.ReadAllText(s.PathFor("tables/tbaiprompttemplate.patch.json"));s.SetValue(background,"不得落盘");s.SetValue(prompt,s.Value(prompt,true)+"另一条规则");prefs.FailNext=true;Reject(delegate{s.Save(false);},"rollback extra tables");Check(File.ReadAllText(s.PathFor("tables/tbnpcbasecfg.patch.json"))==oldbase&&File.ReadAllText(s.PathFor("tables/tbaiprompttemplate.patch.json"))==oldprompt,"all tables restored on failure");
   s.Reload();File.AppendAllText(s.PathFor("tables/tbaiprompttemplate.patch.json")," ");Reject(delegate{s.Save(false);},"template table conflict detected");
   Check(c.Npcs.Count(n=>!String.IsNullOrEmpty(n.Portrait))==1223,"portrait mapping coverage");
   File.WriteAllText(Path.Combine(folder,"EXTENDED-PASSED.txt"),"PASS: cross-table persistence; relation keys/order; unknown fields/languages/tiers preserved; template variables; all-or-nothing preset import; draft-only import; full customization export; expanded rollback/conflict detection; portrait coverage; restored-draft preset preview; relation restore with unknown language.",Encoding.UTF8);
  }
 }
}
