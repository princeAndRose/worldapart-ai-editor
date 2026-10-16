using System;
using System.Collections.Generic;
using System.Linq;
namespace WorldApartAI {
 public sealed class EditChange {
  public int NpcId; public string Language; public string Title; public string Before; public string After; public string FieldKey;
  public override string ToString(){return Title;}
 }
 public sealed class DraftState {
  public Store Store;public string TimeoutText;int savedSeconds;
  Dictionary<int,Dictionary<string,string>> savedStyles;
  Dictionary<string,string> savedExtra;
  public DraftState(Store s){Store=s;Accept();}
  public void Accept(){savedSeconds=Store.Seconds;TimeoutText=savedSeconds.ToString();savedStyles=Store.Styles.ToDictionary(p=>p.Key,p=>new Dictionary<string,string>(p.Value));savedExtra=new Dictionary<string,string>(Store.Extra);}
  public string SavedValue(string key){string value;return savedExtra.TryGetValue(key,out value)?value:Store.Value(key,true);}
  public DraftState ReloadSaved(){return new DraftState(new Store(Store.Root,Store.Catalog,Store.Prefs));}
  public bool TimeoutValid { get {int v;return Int32.TryParse(TimeoutText,out v)&&v>=30&&v<=1800;} }
  public void SetTimeout(string text){TimeoutText=text;int v;if(TimeoutValid&&Int32.TryParse(text,out v))Store.Seconds=v;}
  public string SavedStyle(Npc npc,string lang){Dictionary<string,string> s;string v;return savedStyles.TryGetValue(npc.Id,out s)&&s.TryGetValue(lang,out v)?v:npc.Style.ContainsKey(lang)?npc.Style[lang]:"";}
  public List<EditChange> Changes(){
   var changes=new List<EditChange>();int seconds;
   if(!Int32.TryParse(TimeoutText,out seconds)||seconds!=savedSeconds)changes.Add(new EditChange{NpcId=-1,Title="回复超时",Before=savedSeconds+" 秒",After=TimeoutText+" 秒"});
   foreach(var n in Store.Catalog.Npcs.Where(n=>Store.Styles.ContainsKey(n.Id)||savedStyles.ContainsKey(n.Id)).OrderBy(n=>n.Id))
   foreach(var lang in new[]{"zh-Hans","zh-Hant","en-US"}){
    string old=SavedStyle(n,lang),now=Store.Style(n.Id,lang,false);
    if(old!=now)changes.Add(new EditChange{NpcId=n.Id,Language=lang,Title=n.Name+" / "+n.Id+" / "+lang,Before=old,After=now});
   }
   foreach(string key in Store.Extra.Keys.Union(savedExtra.Keys)){
    var field=Store.Fields[key];string old=SavedValue(key),now=Store.Value(key,false);
    if(old!=now)changes.Add(new EditChange{NpcId=field.NpcId,Language=field.Language,FieldKey=key,Title=(field.NpcId<0?"全局":Store.Catalog.Npcs.First(n=>n.Id==field.NpcId).Name+" / "+field.NpcId)+" / "+field.Title+" / "+field.Language,Before=old,After=now});
   }return changes;
  }
 }
}
