using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WorldApartAI {
 public static class Portraits {
  static ZipArchive zip;static Dictionary<string,string> index;static Dictionary<string,BitmapSource> cache=new Dictionary<string,BitmapSource>();static Queue<string> order=new Queue<string>();
  public static ImageSource Get(string path){
   if(String.IsNullOrEmpty(path))return null;
   try{if(zip==null){zip=new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("AIEditor.Portraits"),ZipArchiveMode.Read);using(var sr=new StreamReader(zip.GetEntry("index.json").Open()))index=Json.Read<Dictionary<string,string>>(sr.ReadToEnd());}
    BitmapSource found;if(cache.TryGetValue(path,out found))return found;string file;if(!index.TryGetValue(path,out file))return null;
    using(var source=zip.GetEntry(file).Open())using(var bytes=new MemoryStream()){source.CopyTo(bytes);bytes.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=bytes;image.DecodePixelWidth=240;image.EndInit();image.Freeze();cache[path]=image;order.Enqueue(path);if(order.Count>96)cache.Remove(order.Dequeue());return image;}
   }catch{return null;}
  }
 }
 public sealed partial class Workspace {
  int galleryPage;const int PageSize=12;List<NpcRow> filteredRows=new List<NpcRow>();string lastQuery="";bool lastOnly;bool galleryVisible=true;double galleryOffset;
  string Field(){var c=Get<ComboBox>("NpcField").SelectedItem as FieldChoice;return c==null?"speakStyle":c.Key;}
  static string FieldLabel(string field){return FieldNames.Npc(field)+(field=="hiddenPersona"||field=="hiddenTrigger"||field=="overDrive"?" · 实验":"");}
  static string FieldEffectHelp(string field){
   if(field=="hiddenPersona")return "实验：当前版本原生角色未开启隐藏人设；修改文字不会开启或解锁。";
   if(field=="hiddenTrigger")return "实验：当前版本原生角色未开启隐藏人设；修改文字不会开启、解锁或新增游戏事件。";
   if(field=="overDrive")return "实验：尚未确认普通聊天会采用此字段，保存文本不代表对话效果已验证。";
   if(field.Contains("Attitude")||field.Contains("attitude"))return "需启用个人关系态度，游戏只采用当前关系阶段的文本；不改变好感度或身份。";
   return "按配置 ID 修改，同配置角色会共用。已有聊天记忆保留。";
  }
  string CurrentKey(){return current==null?null:Store.NpcKey(current.Id,Field(),Field()=="usePersonalIntimacyAttitude"?"value":Lang());}
  string NpcValue(bool original){return Field()=="speakStyle"?store.Style(current.Id,Lang(),original):store.Value(CurrentKey(),original);}
  string SavedNpcValue(){return Field()=="speakStyle"?draft.SavedStyle(current,Lang()):draft.SavedValue(CurrentKey());}
  void SetNpcValue(string value){if(Field()=="speakStyle")store.SetStyle(current.Id,Lang(),value);else store.SetValue(CurrentKey(),value);}
  void InitializeExtended(){
   Get<TextBox>("GallerySearch").TextChanged+=delegate{Get<TextBox>("NpcSearch").Text=Get<TextBox>("GallerySearch").Text;};
   Get<CheckBox>("GalleryOnlyChanged").Checked+=delegate{Get<CheckBox>("OnlyChanged").IsChecked=true;};Get<CheckBox>("GalleryOnlyChanged").Unchecked+=delegate{Get<CheckBox>("OnlyChanged").IsChecked=false;};
   On("GalleryPrev",delegate{galleryPage--;galleryOffset=0;RenderGallery();});On("GalleryNext",delegate{galleryPage++;galleryOffset=0;RenderGallery();});On("BackGallery",delegate{SetGallery(true);});
   Get<ItemsControl>("GalleryItems").AddHandler(System.Windows.Controls.Button.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs e){var button=e.OriginalSource as Button;var row=button==null?null:button.Tag as NpcRow;if(row!=null){Get<ListBox>("NpcList").SelectedItem=row;Get<ListBox>("NpcList").ScrollIntoView(row);SetGallery(false);}}));
   Get<ComboBox>("NpcField").SelectionChanged+=delegate{if(!loading)FillNpc();};
   Get<CheckBox>("UseAttitude").Checked+=delegate{if(!loading&&current!=null){SetNpcValue("true");Refresh();}};Get<CheckBox>("UseAttitude").Unchecked+=delegate{if(!loading&&current!=null){SetNpcValue("false");Refresh();}};
   Get<ComboBox>("PromptSelect").ItemsSource=catalog.Prompts;Get<ComboBox>("PromptLanguage").SelectedIndex=0;
   Get<ComboBox>("PromptSelect").SelectionChanged+=delegate{FillPrompt();};Get<ComboBox>("PromptLanguage").SelectionChanged+=delegate{FillPrompt();};
   Get<TextBox>("PromptEditor").TextChanged+=delegate{if(!loading&&PromptKey()!=null){store.SetValue(PromptKey(),Get<TextBox>("PromptEditor").Text);message="";Refresh();}};
   On("RestorePrompt",delegate{if(PromptKey()!=null){store.SetValue(PromptKey(),store.Value(PromptKey(),true));FillPrompt();Refresh();}});On("UndoPrompt",delegate{if(PromptKey()!=null){store.SetValue(PromptKey(),draft.SavedValue(PromptKey()));FillPrompt();Refresh();}});
   On("ExportPreset",ExportPreset);On("ImportPreset",ImportPreset);Get<ComboBox>("PromptSelect").SelectedIndex=0;
   SetGallery(true);RenderGallery();
  }
  void SetGallery(bool show){if(!show&&galleryVisible)galleryOffset=Get<ScrollViewer>("GalleryScroll").VerticalOffset;galleryVisible=show;Get<FrameworkElement>("NpcGallery").Visibility=show?Visibility.Visible:Visibility.Collapsed;Get<FrameworkElement>("NpcEditorPane").Visibility=show?Visibility.Collapsed:Visibility.Visible;if(show)RenderGallery();}
  void RenderGallery(){if(Get<ItemsControl>("GalleryItems")==null)return;int pages=Math.Max(1,(filteredRows.Count+PageSize-1)/PageSize);galleryPage=Math.Max(0,Math.Min(galleryPage,pages-1));Get<ItemsControl>("GalleryItems").ItemsSource=filteredRows.Skip(galleryPage*PageSize).Take(PageSize).ToList();Text("GalleryCount").Text=filteredRows.Count+" 位人物配置";Text("GalleryPageNumber").Text=(galleryPage+1)+" / "+pages;Button("GalleryPrev").IsEnabled=galleryPage>0;Button("GalleryNext").IsEnabled=galleryPage<pages-1;Text("GalleryEmpty").Visibility=filteredRows.Count==0?Visibility.Visible:Visibility.Collapsed;Get<ScrollViewer>("GalleryScroll").UpdateLayout();Get<ScrollViewer>("GalleryScroll").ScrollToVerticalOffset(galleryOffset);}
  bool CustomNpc(int id){return store.Styles.ContainsKey(id)||store.Extra.Keys.Any(k=>k.StartsWith("npc/"+id+"/",StringComparison.Ordinal));}
  void FillNpcExtended(){
   string field=Field();loading=true;
   try{
    bool has=current!=null;Text("NpcName").Text=has?current.Name:"没有匹配的 NPC";Text("NpcIdentity").Text=has?(String.IsNullOrEmpty(current.Title)?"无称号":current.Title)+"   /   配置 ID "+current.Id:"调整搜索或取消筛选。";
    Get<Image>("DetailPortrait").Source=has?Portraits.Get(current.Portrait):null;
    var choices=new List<FieldChoice>{new FieldChoice{Key="speakStyle",Label="说话风格"}};if(has&&current.Fields!=null)choices.AddRange(current.Fields.Keys.Select(k=>new FieldChoice{Key=k,Label=FieldLabel(k)}));
    Get<ComboBox>("NpcField").ItemsSource=choices;Get<ComboBox>("NpcField").SelectedItem=choices.FirstOrDefault(c=>c.Key==field)??choices[0];
    System.Windows.Automation.AutomationProperties.SetName(Get<TextBox>("StyleEditor"),FieldNames.Npc(Field())+"编辑框");System.Windows.Automation.AutomationProperties.SetName(Get<TextBox>("OriginalStyle"),"原版"+FieldNames.Npc(Field()));
    bool toggle=Field()=="usePersonalIntimacyAttitude";Get<TextBox>("StyleEditor").Visibility=toggle?Visibility.Collapsed:Visibility.Visible;Get<CheckBox>("UseAttitude").Visibility=toggle?Visibility.Visible:Visibility.Collapsed;Get<CheckBox>("UseAttitude").IsEnabled=has;
    Get<TextBox>("StyleEditor").IsEnabled=has;Get<TextBox>("StyleEditor").MaxLength=Field()=="speakStyle"?8000:24000;Get<ComboBox>("Language").IsEnabled=has&&!toggle;Get<ComboBox>("NpcField").IsEnabled=has;
    Get<TextBox>("StyleEditor").Text=has?NpcValue(false):"";Get<TextBox>("OriginalStyle").Text=has?NpcValue(true):"";Get<CheckBox>("UseAttitude").IsChecked=has&&toggle&&NpcValue(false)=="true";
    Text("FieldHelp").Text=FieldEffectHelp(Field());
   }finally{loading=false;}Refresh();
  }
  string PromptKey(){var p=Get<ComboBox>("PromptSelect").SelectedItem as PromptTemplate;return p==null?null:Store.PromptKey(p.Key,new[]{"zh-Hans","zh-Hant","en-US"}[Math.Max(0,Get<ComboBox>("PromptLanguage").SelectedIndex)]);}
  void FillPrompt(){var key=PromptKey();if(key==null)return;loading=true;try{Get<TextBox>("PromptEditor").Text=store.Value(key,false);Get<TextBox>("PromptOriginal").Text=store.Value(key,true);string variables=store.VariableHint(key);Text("PromptHint").Text=(key.Contains("/state.chat_relation_actions/")?"本轮关系交互由游戏提供可用动作；文字修改不会绕过关系条件。\n":"")+(key.Contains("/format.")?"输出格式供游戏解析，修改正文也可能导致解析失败。\n":"")+(variables.Length>0?"保存时必须保留变量："+variables:"此模板没有占位变量。")+"\n"+(key.Contains("/task.chat/")?"保存后重启游戏。新闲聊已验证采用此模板，已有对话可能保留旧任务。":"保存后重启游戏；已有对话可能保留旧模板，请用新对话验证。");}finally{loading=false;}Refresh();}
  void RefreshPrompt(){var key=PromptKey();if(key==null)return;Text("PromptState").Text=store.Value(key,false)!=draft.SavedValue(key)?"待保存":"与上次保存一致";Button("UndoPrompt").IsEnabled=store.Value(key,false)!=draft.SavedValue(key);Button("RestorePrompt").IsEnabled=store.Value(key,false)!=store.Value(key,true);}
  void ExportPreset(){if(!draft.TimeoutValid)throw new InvalidDataException("请先修正超时值。");store.ValidateExtra();var d=new Microsoft.Win32.SaveFileDialog{Title="导出当前配置（含草稿）",Filter="AI 编辑器预设|*.json",FileName="不问凡尘-AI预设.json"};if(d.ShowDialog(window)!=true)return;File.WriteAllText(d.FileName,Json.Write(store.ExportPreset()),new UTF8Encoding(false));message="预设已导出，包含当前自定义配置与超时值。";Refresh();}
  void ImportPreset(){
   if(!draft.TimeoutValid)throw new InvalidDataException("请先修正超时值，再导入预设。");
   var d=new Microsoft.Win32.OpenFileDialog{Title="导入 AI 编辑器预设",Filter="AI 编辑器预设|*.json"};if(d.ShowDialog(window)!=true)return;if(new FileInfo(d.FileName).Length>16000000)throw new InvalidDataException("预设文件过大。");
   var preset=Json.Read<EditorPreset>(File.ReadAllText(d.FileName,Encoding.UTF8));var preview=new Store(store.Root,catalog,new MemoryPrefs());store.CopyDraftTo(preview);var baseline=new DraftState(preview);preview.ImportPreset(preset);baseline.TimeoutText=preview.Seconds.ToString();var changes=baseline.Changes();
   if(changes.Count==0){message="预设与当前草稿一致，无需导入。";Refresh();return;}
   var dialog=new Window{Owner=window,Title="导入预设 · 检查合并内容",Width=780,Height=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,Resources=window.Resources,FontFamily=window.FontFamily};var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});dialog.Content=grid;grid.Children.Add(new TextBlock{Text="以下项目将替换当前草稿；未列出的项目保持不变。导入后仍需保存才会写入游戏。",Margin=new Thickness(0,0,0,15)});
   var text=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Text=String.Join("\n\n",changes.Select(c=>c.Title+"\n当前："+c.Before+"\n导入："+c.After))};Grid.SetRow(text,1);grid.Children.Add(text);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};Grid.SetRow(buttons,2);grid.Children.Add(buttons);var cancel=new Button{Content="取消",IsCancel=true,Margin=new Thickness(0,0,12,0)};buttons.Children.Add(cancel);var accept=new Button{Content="合并到草稿"};buttons.Children.Add(accept);cancel.Click+=delegate{dialog.DialogResult=false;};accept.Click+=delegate{dialog.DialogResult=true;};if(dialog.ShowDialog()!=true)return;
   store.ImportPreset(preset);SetTimeout(store.Seconds);Filter();FillNpc();FillPrompt();message="预设已合并到草稿。检查后点“保存并启用”。";Refresh();
  }
  void Locate(EditChange change){
   if(change.FieldKey!=null&&change.NpcId<0){Get<RadioButton>("NavPrompts").IsChecked=true;var f=store.Fields[change.FieldKey];Get<ComboBox>("PromptSelect").SelectedItem=catalog.Prompts.First(p=>p.Key==f.TemplateKey);Get<ComboBox>("PromptLanguage").SelectedIndex=Array.IndexOf(new[]{"zh-Hans","zh-Hant","en-US"},f.Language);FillPrompt();return;}
   if(change.NpcId<0){Get<RadioButton>("NavRequests").IsChecked=true;Get<TextBox>("TimeoutInput").Focus();return;}
   Get<RadioButton>("NavNpc").IsChecked=true;Get<TextBox>("NpcSearch").Text=change.NpcId.ToString();Get<CheckBox>("OnlyChanged").IsChecked=false;Get<ListBox>("NpcList").SelectedItem=rows.First(r=>r.Npc.Id==change.NpcId);SetGallery(false);string field=change.FieldKey==null?"speakStyle":store.Fields[change.FieldKey].Field;Get<ComboBox>("NpcField").SelectedItem=Get<ComboBox>("NpcField").Items.Cast<FieldChoice>().First(f=>f.Key==field);if(change.Language!="value")Get<ComboBox>("Language").SelectedIndex=Array.IndexOf(new[]{"zh-Hans","zh-Hant","en-US"},change.Language);FillNpc();
  }
 }
}


