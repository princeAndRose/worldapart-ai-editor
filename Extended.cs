using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace WorldApartAI {
 public sealed class PromptTemplate {public string Key;public Dictionary<string,string> Text; public string Name{get{return FieldNames.Prompt(Key);}} public override string ToString(){return Name+" · "+Key;}}
 public static class FieldNames {
  public static string Npc(string key){
   switch(key){case "speakStyle":return "说话风格";case "propPlot":return "人物背景";case "hiddenPersona":return "隐藏人设";case "hiddenTrigger":return "隐藏人设触发条件";case "overDrive":return "特殊指令（overDrive）";case "usePersonalIntimacyAttitude":return "使用个人关系态度";}
   var parts=key.Split(':');return (parts[0]=="daoCompanionAttitudeByIntimacy"?"道侣态度":"亲密度态度")+" · 阶段 "+parts.Last();
  }
  public static string Prompt(string key){var names=new Dictionary<string,string>{{"rule.worldview","世界观"},{"task.chat","闲聊任务"},{"task.persuade","说服任务"},{"task.topic","话题任务"},{"general_requirements","通用要求"},{"format.chat","闲聊输出格式"},{"format.persuade","说服输出格式"},{"format.topic","话题输出格式"},{"state.chat_boundary","对话警告与结束"},{"state.chat_relation_actions","本轮关系交互"},{"rule.chat_repeat_correction","重复回复纠正"}};string n;if(names.TryGetValue(key,out n))return n;return key.Replace("relation.","关系 · ").Replace("daocompanion","道侣").Replace("disciple","徒弟").Replace("master","师父").Replace("enemy","敌人").Replace("friend","朋友").Replace(".name"," · 名称").Replace(".desc"," · 描述").Replace(".intimacy."," · 亲密阶段 ");}
 }
 public sealed class FieldRef {
  public string Key,Field,Language,Original,TemplateKey;public int NpcId;
  public string Title{get{return NpcId<0?FieldNames.Prompt(TemplateKey):FieldNames.Npc(Field);}}
 }
 public sealed class FieldChoice {public string Key;public string Label;public override string ToString(){return Label;}}
 public sealed class PresetValue {public string Key;public string Value;}
 public sealed class EditorPreset {public string Format;public int Version;public string MetadataHash;public int TimeoutSeconds;public List<PresetValue> Values;}
 public sealed partial class Store {
  public Dictionary<string,FieldRef> Fields=new Dictionary<string,FieldRef>();
  public Dictionary<string,string> Extra=new Dictionary<string,string>();
  Dictionary<string,object> basePatch,promptPatch;
  public static string NpcKey(int id,string field,string lang){return "npc/"+id+"/"+field+"/"+lang;}
  public static string PromptKey(string key,string lang){return "prompt/"+key+"/"+lang;}
  public string Value(string key,bool original){FieldRef f;if(!Fields.TryGetValue(key,out f))throw new InvalidDataException("不支持的字段："+key);string value;return !original&&Extra.TryGetValue(key,out value)?value:f.Original;}
  public void SetValue(string key,string value){if(!Fields.ContainsKey(key)||value==null||value.Length>24000)throw new InvalidDataException("字段不支持或文本超过 24000 字符。");if(Fields[key].Field=="usePersonalIntimacyAttitude"&&value!="true"&&value!="false")throw new InvalidDataException("关系态度开关只能为 true 或 false。");if(value==Fields[key].Original)Extra.Remove(key);else Extra[key]=value;}
  static Dictionary<string,object> FindRow(Dictionary<string,object> patch,string key,object id){return Json.List(patch["upsert"]).Select(Json.Dict).FirstOrDefault(r=>Convert.ToString(r[key])==Convert.ToString(id));}
  void ReloadExtra(){
   basePatch=Load(Files[3],Patch("tbnpcbasecfg","id"));promptPatch=Load(Files[4],Patch("tbaiprompttemplate","templateKey"));ValidatePatch(basePatch,"tbnpcbasecfg","id");
   if(Convert.ToString(promptPatch["table"])!="tbaiprompttemplate"||!Json.List(promptPatch["keyFields"]).Select(Convert.ToString).SequenceEqual(new[]{"templateKey"}))throw new InvalidDataException("全局提示词补丁结构不兼容。");
   var keys=new HashSet<string>();foreach(var row in Json.List(promptPatch["upsert"]).Select(Json.Dict))if(!row.ContainsKey("templateKey")||!keys.Add(Convert.ToString(row["templateKey"])))throw new InvalidDataException("全局提示词定位字段缺失或重复。");
   Fields.Clear();Extra.Clear();
   foreach(var n in Catalog.Npcs)if(n.Fields!=null)foreach(var field in n.Fields)foreach(var lang in field.Value){
    var f=new FieldRef{NpcId=n.Id,Field=field.Key,Language=lang.Key,Original=lang.Value,Key=NpcKey(n.Id,field.Key,lang.Key)};Fields.Add(f.Key,f);
    var row=FindRow(field.Key=="propPlot"?basePatch:persona,field.Key=="propPlot"?"id":"npcId",n.Id);ReadExtra(f,row);
   }
   if(Catalog.Prompts!=null)foreach(var p in Catalog.Prompts)foreach(var lang in p.Text){var f=new FieldRef{NpcId=-2,TemplateKey=p.Key,Field="templateText",Language=lang.Key,Original=lang.Value,Key=PromptKey(p.Key,lang.Key)};Fields.Add(f.Key,f);ReadExtra(f,FindRow(promptPatch,"templateKey",p.Key));}
  }
  void ReadExtra(FieldRef f,Dictionary<string,object> row){
   if(row==null)return;var parts=f.Field.Split(':');object data;if(!row.TryGetValue(parts[0],out data))return;
   if(parts.Length==2){var pairs=Json.List(data).Select(Json.List).ToList();if(pairs.Any(p=>p.Count!=2)||pairs.Select(p=>Convert.ToString(p[0])).Distinct().Count()!=pairs.Count)throw new InvalidDataException("关系阶段结构不兼容。");var pair=pairs.FirstOrDefault(p=>Convert.ToString(p[0])==parts[1]);if(pair==null)return;data=pair[1];}
   if(f.Language=="value"){if(!(data is bool))throw new InvalidDataException("关系开关格式不兼容。");SetValue(f.Key,((bool)data)?"true":"false");}
   else{object value;if(Json.Dict(data).TryGetValue(f.Language,out value)){if(!(value is string))throw new InvalidDataException("提示词必须是文本。");SetValue(f.Key,(string)value);}}
  }
  static string[] Variables(string text){return Regex.Matches(text??"",@"\{[A-Z][A-Z_0-9]*\}").Cast<Match>().Select(m=>m.Value).Distinct().OrderBy(s=>s).ToArray();}
  public void ValidateExtra(){foreach(var e in Extra){var f=Fields[e.Key];if(e.Value==null||e.Value.Length>24000)throw new InvalidDataException("提示词过长。");if(f.NpcId<0&&!Variables(e.Value).SequenceEqual(Variables(f.Original)))throw new InvalidDataException(f.Title+" / "+f.Language+" 的模板变量与原版不同。请保留："+String.Join("、",Variables(f.Original)));}}
  public string VariableHint(string key){return String.Join("  ",Variables(Fields[key].Original));}
  void BuildExtra(Dictionary<string,object> p,Dictionary<string,byte[]> output){
   ValidateExtra();var b=Json.Copy(basePatch);var t=Json.Copy(promptPatch);
   // Only groups that appear in the original patch or have an override are rebuilt.
   foreach(var group in Fields.Values.GroupBy(f=>(f.NpcId<0?"prompt/"+f.TemplateKey:"npc/"+f.NpcId)+"/"+f.Field.Split(':')[0])){
    var first=group.First();var target=first.NpcId<0?t:first.Field=="propPlot"?b:p;string idKey=first.NpcId<0?"templateKey":first.Field=="propPlot"?"id":"npcId";object id=first.NpcId<0?(object)first.TemplateKey:first.NpcId;
    string field=first.Field.Split(':')[0];var rows=Json.List(target["upsert"]);var row=rows.Select(Json.Dict).FirstOrDefault(r=>Convert.ToString(r[idKey])==Convert.ToString(id));bool changed=group.Any(f=>Extra.ContainsKey(f.Key));
    if(row==null&&!changed)continue;
    if(row==null){row=new Dictionary<string,object>{{idKey,id}};rows.Add(row);}
    object existing;row.TryGetValue(field,out existing);
    if(field=="usePersonalIntimacyAttitude"){row.Remove(field);if(changed)row[field]=Value(first.Key,false)=="true";}
    else if(first.Field.Contains(":")){
     var pairs=existing==null?new List<object>():Json.List(existing);var tierOrder=pairs.Select(o=>Convert.ToString(Json.List(o)[0])).Concat(group.Select(f=>f.Field.Split(':')[1])).Distinct().ToList();
     foreach(var tier in group.GroupBy(f=>f.Field.Split(':')[1])){
      var pair=pairs.Select(Json.List).FirstOrDefault(a=>Convert.ToString(a[0])==tier.Key);var texts=pair==null?new Dictionary<string,object>():Json.Dict(pair[1]);
      foreach(var f in tier)texts.Remove(f.Language);
      bool customized=tier.Any(f=>Extra.ContainsKey(f.Key));if(customized)foreach(var f in tier)texts[f.Language]=Value(f.Key,false);
      if(pair!=null)pairs.RemoveAll(o=>Convert.ToString(Json.List(o)[0])==tier.Key);
      if(texts.Count>0)pairs.Add(new object[]{Int32.Parse(tier.Key),texts});
     }
     row.Remove(field);if(pairs.Count>0){
      // Relationship arrays replace the whole game value: fill every original tier.
      foreach(var tier in group.GroupBy(f=>f.Field.Split(':')[1])){
       var pair=pairs.Select(Json.List).FirstOrDefault(a=>Convert.ToString(a[0])==tier.Key);
       if(pair==null)pairs.Add(new object[]{Int32.Parse(tier.Key),tier.ToDictionary(f=>f.Language,f=>(object)Value(f.Key,false))});
       else foreach(var f in tier)Json.Dict(pair[1])[f.Language]=Value(f.Key,false);
      }
      row[field]=pairs.OrderBy(o=>tierOrder.IndexOf(Convert.ToString(Json.List(o)[0]))).ToList();
     }
    }else{
     var texts=existing==null?new Dictionary<string,object>():Json.Dict(existing);foreach(var f in group)texts.Remove(f.Language);if(changed)foreach(var f in group)texts[f.Language]=Value(f.Key,false);
     row.Remove(field);if(texts.Count>0)row[field]=texts;
    }
    if(row.Count==1)rows.Remove(row);target["upsert"]=rows;
   }
   output[Files[2]]=Utf8.GetBytes(Json.Write(p));output[Files[3]]=Utf8.GetBytes(Json.Write(b));output[Files[4]]=Utf8.GetBytes(Json.Write(t));
  }
  public void CopyDraftTo(Store target){target.Styles=Styles.ToDictionary(n=>n.Key,n=>new Dictionary<string,string>(n.Value));target.Extra=new Dictionary<string,string>(Extra);target.Seconds=Seconds;}
  public EditorPreset ExportPreset(){var values=Extra.Select(e=>new PresetValue{Key=e.Key,Value=e.Value}).ToList();foreach(var n in Styles)foreach(var lang in n.Value)if(new[]{"zh-Hans","zh-Hant","en-US"}.Contains(lang.Key)&&lang.Value!=Style(n.Key,lang.Key,true))values.Add(new PresetValue{Key=NpcKey(n.Key,"speakStyle",lang.Key),Value=lang.Value});return new EditorPreset{Format="WorldApart.AI.Editor.Preset",Version=1,MetadataHash=Catalog.MetadataHash,TimeoutSeconds=Seconds,Values=values};}
  public void ImportPreset(EditorPreset preset){
   if(preset==null||preset.Format!="WorldApart.AI.Editor.Preset"||preset.Version!=1||Catalog.MatchVersion(preset.MetadataHash)==null||preset.Values==null||preset.Values.Count>50000||preset.TimeoutSeconds<30||preset.TimeoutSeconds>1800)throw new InvalidDataException("预设格式、游戏版本或超时值不兼容。");
   var seen=new HashSet<string>();foreach(var v in preset.Values){if(v==null||v.Key==null||v.Value==null||!seen.Add(v.Key))throw new InvalidDataException("预设存在空值或重复字段。");var parts=v.Key.Split('/');int id;
    if(parts.Length==4&&parts[0]=="npc"&&parts[2]=="speakStyle"&&Int32.TryParse(parts[1],out id)&&npcs.ContainsKey(id)&&new[]{"zh-Hans","zh-Hant","en-US"}.Contains(parts[3])){if(v.Value.Length>8000)throw new InvalidDataException("风格文本过长。");}
    else{FieldRef f;if(!Fields.TryGetValue(v.Key,out f)||v.Value.Length>24000)throw new InvalidDataException("预设包含不支持的字段。");if(f.Language=="value"&&v.Value!="true"&&v.Value!="false")throw new InvalidDataException("预设开关值不合法。");if(f.NpcId<0&&!Variables(v.Value).SequenceEqual(Variables(f.Original)))throw new InvalidDataException("预设缺少或增加了模板变量："+f.Title);}
   }
   foreach(var v in preset.Values){if(Fields.ContainsKey(v.Key))SetValue(v.Key,v.Value);else{var parts=v.Key.Split('/');SetStyle(Int32.Parse(parts[1]),parts[3],v.Value);}}Seconds=preset.TimeoutSeconds;
  }
 }
}
