using System;
using System.IO;
using System.Linq;
using System.Text;
namespace WorldApartAI {
 static class DraftTests {
  static void Assert(bool value,string name){if(!value)throw new Exception("Draft test failed: "+name);}
  public static void Run(string folder,Catalog catalog){
   var store=new Store(Path.Combine(folder,"draft-"+Guid.NewGuid().ToString("N")),catalog,new MemoryPrefs());
   var draft=new DraftState(store);var npc=catalog.Npcs.First(n=>n.Id==100000);var twin=catalog.Npcs.First(n=>n.Id==190000);
   Assert(npc.Name==twin.Name&&npc.Title!=twin.Title,"same-name identities distinct");
   Assert(draft.Changes().Count==0,"initial clean");
   draft.SetTimeout("300");Assert(draft.Changes().Count==1,"timeout dirty");
   draft.SetTimeout("180");Assert(draft.Changes().Count==0,"reverted timeout clean");
   foreach(string text in new[]{"","abc","29","1801"}){draft.SetTimeout(text);Assert(!draft.TimeoutValid&&draft.Changes().Count==1,"invalid timeout "+text);}
   draft.SetTimeout("180");store.SetStyle(npc.Id,"zh-Hans","自定义已保存风格");store.Save(false);draft.Accept();
   store.SetStyle(npc.Id,"zh-Hans","新草稿");Assert(draft.Changes().Single().Before=="自定义已保存风格","comparison uses saved not original");
   store.SetStyle(npc.Id,"zh-Hans",draft.SavedStyle(npc,"zh-Hans"));Assert(draft.Changes().Count==0,"undo returns saved");
   store.SetStyle(npc.Id,"zh-Hans",store.Style(npc.Id,"zh-Hans",true));Assert(draft.Changes().Count==1,"restore original remains a change");
   store.SetStyle(twin.Id,"en-US","A second draft");Assert(draft.Changes().Count==2,"multi NPC language count");
   string snapshot=Json.Write(draft.Changes());store.Seconds=300;draft.SetTimeout("300");Assert(draft.Changes().Count==3,"timeout included");
   Assert(Json.Write(draft.Changes()).Contains("190000"),"export preserves identity");
   string personaPath=store.PathFor("tables/tbnpcaipersona.patch.json"),good=File.ReadAllText(personaPath);
   File.WriteAllText(personaPath,"{\"table\":\"tbnpcaipersona\",\"keyFields\":[\"npcId\"],\"upsert\":[{\"npcId\":100000,\"speakStyle\":17}]}");
   bool failed=false;try{draft.ReloadSaved();}catch{failed=true;}
   Assert(failed&&draft.Changes().Count==3&&draft.Changes().Any(c=>c.After=="A second draft"),"damaged external patch preserves complete draft");
   File.WriteAllText(personaPath,good);draft=draft.ReloadSaved();Assert(draft.Changes().Count==0,"discard reload");
   File.WriteAllText(Path.Combine(folder,"DRAFT-PASSED.txt"),"PASS: same-name identities; exact dirty count; invalid input; multi-NPC/language changes; saved/original distinction; revert clean; export identity; discard reload.",Encoding.UTF8);
  }
 }
}
