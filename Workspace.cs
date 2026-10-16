using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
[assembly: AssemblyTitle("不问凡尘 AI 编辑器")]
[assembly: AssemblyVersion("0.3.1.0")]
[assembly: AssemblyFileVersion("0.3.1.0")]
namespace WorldApartAI {
 public sealed class NpcRow : INotifyPropertyChanged {
  public Npc Npc; public ImageSource Portrait{get{return Portraits.Get(Npc.Portrait);}} public string Initial{get{return String.IsNullOrEmpty(Npc.Name)?"人":Npc.Name.Substring(0,1);}} public string Name{get{return Npc.Name;}}public string Subtitle{get{return (String.IsNullOrWhiteSpace(Npc.Title)?"无称号":Npc.Title)+"  ·  "+Npc.Id;}}
  string badge="";public string Badge{get{return badge;}set{if(badge!=value){badge=value;if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs("Badge"));}}}
  public event PropertyChangedEventHandler PropertyChanged;
  public override string ToString(){return Name+" / "+Subtitle;}
 }
 public sealed partial class Workspace {
  Window window;Catalog catalog;Store store;DraftState draft;List<NpcRow> rows;Npc current;bool loading,compatible,gameRunning,enabled;string gameRoot="",settingsFile,message="",matchedGameVersion;string prefError;
  DispatcherTimer timer;string activePage="Requests";Dictionary<string,FrameworkElement> pages;
  T Get<T>(string name)where T:class{return window.FindName(name)as T;}
  TextBlock Text(string name){return Get<TextBlock>(name);}Button Button(string name){return Get<Button>(name);}
  public Workspace(Catalog c,bool testMode){
   catalog=c;
   string modRoot=testMode?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-data",Store.ModId):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Nuverse","WorldApart","Mods",Store.ModId);
   store=new Store(modRoot,c,testMode?(IPrefs)new MemoryPrefs{Bytes=Encoding.UTF8.GetBytes(Store.Entry+"\0")}:new RealPrefs());draft=new DraftState(store);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AIEditor.Workspace"))window=(Window)XamlReader.Load(stream);
   if(testMode)window.Title+=" [交互测试]";
   rows=c.Npcs.OrderBy(n=>n.Id).Select(n=>new NpcRow{Npc=n}).ToList();settingsFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WorldApart.AI.Editor","settings.json");
   pages=new Dictionary<string,FrameworkElement>{{"Requests",Get<FrameworkElement>("RequestsPage")},{"Npc",Get<FrameworkElement>("NpcPage")},{"Settings",Get<FrameworkElement>("SettingsPage")},{"Roadmap",Get<FrameworkElement>("RoadmapPage")},{"Prompts",Get<FrameworkElement>("PromptsPage")}};
   foreach(var pair in pages){string name=pair.Key;Get<RadioButton>("Nav"+name).Checked+=delegate{Show(name);};}
   Get<TextBox>("TimeoutInput").Text=draft.TimeoutText;
   Get<TextBox>("TimeoutInput").TextChanged+=delegate{if(!loading){draft.SetTimeout(Get<TextBox>("TimeoutInput").Text);message="";Refresh();}};
   Get<TextBox>("TimeoutInput").LostKeyboardFocus+=delegate{if(draft.TimeoutValid)SetTimeout(store.Seconds);};
   On("Preset180",delegate{SetTimeout(180);});On("Preset300",delegate{SetTimeout(300);});On("Preset600",delegate{SetTimeout(600);});On("TimeoutRestore",delegate{SetTimeout(30);});
   Get<TextBox>("NpcSearch").TextChanged+=delegate{Filter();};Get<CheckBox>("OnlyChanged").Checked+=delegate{Filter();};Get<CheckBox>("OnlyChanged").Unchecked+=delegate{Filter();};
   Get<ListBox>("NpcList").SelectionChanged+=delegate{var row=Get<ListBox>("NpcList").SelectedItem as NpcRow;current=row==null?null:row.Npc;FillNpc();};
   Get<ComboBox>("Language").SelectedIndex=0;Get<ComboBox>("Language").SelectionChanged+=delegate{FillNpc();};
   Get<Expander>("OriginalExpander").Expanded+=delegate{Get<Grid>("NpcDetailGrid").RowDefinitions[3].Height=new GridLength(1.5,GridUnitType.Star);Get<Grid>("NpcDetailGrid").RowDefinitions[5].Height=new GridLength(1,GridUnitType.Star);};
   Get<Expander>("OriginalExpander").Collapsed+=delegate{Get<Grid>("NpcDetailGrid").RowDefinitions[5].Height=GridLength.Auto;};
   Get<TextBox>("StyleEditor").TextChanged+=delegate{if(!loading&&current!=null){SetNpcValue(Get<TextBox>("StyleEditor").Text);message="";Refresh();}};
   On("RestoreNpc",delegate{if(current!=null){SetNpcValue(NpcValue(true));FillNpc();message="已在草稿中恢复此字段原版，保存后生效。";Refresh();}});
   On("UndoNpc",delegate{if(current!=null){SetNpcValue(SavedNpcValue());FillNpc();Refresh();}});
   On("SaveChanges",Save);On("ReviewChanges",Review);On("DiscardChanges",Discard);On("ChooseGame",ChooseGame);On("OpenMod",delegate{OpenFolder(store.Root);});On("OpenBackup",delegate{OpenFolder(Path.Combine(store.Root,"editor-backups"));});On("ExportDraft",Export);On("DisableMod",Disable);
   DetectGame();InitializeExtended();Filter();Refresh();timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=delegate{Refresh();};timer.Start();
   window.Closed+=delegate{timer.Stop();};
   window.Closing+=delegate(object sender,CancelEventArgs e){if(draft.Changes().Count>0&&MessageBox.Show(window,"还有未保存的更改。放弃这些更改并退出？","未保存的更改",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;};
   window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.S){e.Handled=true;if(Button("SaveChanges").IsEnabled)Guard(Save);}if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.F){e.Handled=true;Get<RadioButton>("NavNpc").IsChecked=true;Get<TextBox>(galleryVisible?"GallerySearch":"NpcSearch").Focus();Get<TextBox>(galleryVisible?"GallerySearch":"NpcSearch").SelectAll();}};
  }
  public void Run(bool minimumTestSize){if(minimumTestSize){window.Width=window.MinWidth;window.Height=window.MinHeight;}new Application().Run(window);}
  void On(string name,Action action){Button(name).Click+=delegate{Guard(action);};}
  void Guard(Action action){try{action();}catch(Exception e){message=e.Message;Refresh();MessageBox.Show(window,e.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}}
  void Show(string name){activePage=name;foreach(var p in pages)p.Value.Visibility=p.Key==name?Visibility.Visible:Visibility.Collapsed;}
  string Lang(){return new[]{"zh-Hans","zh-Hant","en-US"}[Math.Max(0,Get<ComboBox>("Language").SelectedIndex)];}
  void SetTimeout(int seconds){Get<TextBox>("TimeoutInput").Text=seconds.ToString();draft.SetTimeout(seconds.ToString());Refresh();}
  void Filter(){
   int id=current==null?-1:current.Id;string q=Get<TextBox>("NpcSearch").Text.Trim();bool only=Get<CheckBox>("OnlyChanged").IsChecked==true;var changed=new HashSet<int>(draft.Changes().Select(c=>c.NpcId));
   var filtered=rows.Where(r=>(q.Length==0||r.ToString().IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)&&(!only||CustomNpc(r.Npc.Id)||changed.Contains(r.Npc.Id))).ToList();
   if(q!=lastQuery||only!=lastOnly){galleryPage=0;galleryOffset=0;lastQuery=q;lastOnly=only;}filteredRows=filtered;Get<TextBox>("GallerySearch").Text=q;Get<CheckBox>("GalleryOnlyChanged").IsChecked=only;RenderGallery();
   var list=Get<ListBox>("NpcList");list.ItemsSource=filtered;list.SelectedItem=filtered.FirstOrDefault(r=>r.Npc.Id==id)??filtered.FirstOrDefault();
   Text("NpcCount").Text=filtered.Count+" / "+catalog.Npcs.Count+" 个 NPC 配置";if(filtered.Count==0){current=null;FillNpc();}
  }
  void FillNpc(){FillNpcExtended();}
  bool Running(){var processes=Process.GetProcessesByName("WorldApart");bool running=processes.Length>0;foreach(var p in processes)p.Dispose();return running;}
  void Refresh(){
   if(window==null)return;
   var changes=draft.Changes();var changedIds=new HashSet<int>(changes.Select(c=>c.NpcId));foreach(var row in rows)row.Badge=changedIds.Contains(row.Npc.Id)?"草稿":CustomNpc(row.Npc.Id)?"已自定义":"";
   gameRunning=Running();prefError=null;try{enabled=store.IsEnabled();}catch(Exception e){prefError=e.Message;enabled=false;}
   Text("RuntimeState").Text=gameRunning?"游戏运行中 · 退出后可保存":"游戏未运行";
   Text("ModState").Text=prefError!=null?"无法读取配置状态":enabled?"配置已启用":"配置未启用";
   Text("DraftStatus").Text=changes.Count==0?"没有未保存的修改":"待保存 "+changes.Count+" 项更改";
   Button("ReviewChanges").IsEnabled=changes.Count>0;Button("DiscardChanges").IsEnabled=changes.Count>0;
   Button("SaveChanges").IsEnabled=compatible&&!gameRunning&&prefError==null&&draft.TimeoutValid&&(changes.Count>0||!enabled);
   Button("SaveChanges").Content=changes.Count==0&&!enabled?"启用配置":"保存并启用";Button("DisableMod").IsEnabled=enabled&&!gameRunning&&prefError==null;
   Text("ActionHint").Text=!draft.TimeoutValid?"回复超时需要填写 30–1800 之间的整数。":!compatible?"游戏目录或版本不匹配，请在“安装与备份”中检查。":prefError!=null?prefError:gameRunning?"请完整退出游戏后保存。当前更改会留在编辑器中。":message.Length>0?message:"修改后保存，重新启动游戏生效。";
   Text("TimeoutError").Visibility=draft.TimeoutValid?Visibility.Collapsed:Visibility.Visible;Text("TimeoutError").Text="请输入 30–1800 之间的整数。";
   int seconds;Text("TimeoutDuration").Text=Int32.TryParse(draft.TimeoutText,out seconds)&&draft.TimeoutValid?(seconds/60>0?seconds/60+" 分钟"+(seconds%60>0?" "+seconds%60+" 秒":""):seconds+" 秒"):"";
   if(current!=null){
    string now=NpcValue(false),saved=SavedNpcValue(),orig=NpcValue(true);
    Text("NpcDraftState").Text=now!=saved?(now==orig?"待保存：恢复原版":"待保存：自定义内容"):now==orig?"与原版一致":"使用已保存的自定义内容";
    Text("CharacterCount").Text=now.Length+" / "+(Field()=="speakStyle"?8000:24000);Button("UndoNpc").IsEnabled=now!=saved;Button("RestoreNpc").IsEnabled=now!=orig;
   }else{Text("NpcDraftState").Text="";Text("CharacterCount").Text="";Button("UndoNpc").IsEnabled=false;Button("RestoreNpc").IsEnabled=false;}
   RefreshPrompt();
  }
  void DetectGame(){
   try{if(File.Exists(settingsFile))gameRoot=Convert.ToString(Json.Obj(File.ReadAllText(settingsFile))["gameRoot"]);}catch{gameRoot="";}
   if(String.IsNullOrEmpty(gameRoot)){var d=new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);while(d!=null){if(File.Exists(Path.Combine(d.FullName,"WorldApart.exe"))){gameRoot=d.FullName;break;}d=d.Parent;}}VerifyGame();
  }
  void VerifyGame(){
   matchedGameVersion=null;if(!String.IsNullOrEmpty(gameRoot)){string meta=Path.Combine(gameRoot,"WorldApart_Data","il2cpp_data","Metadata","global-metadata.dat");if(File.Exists(Path.Combine(gameRoot,"WorldApart.exe"))&&File.Exists(meta))matchedGameVersion=catalog.MatchVersion(Store.FileHash(meta));}
   compatible=matchedGameVersion!=null;
   Text("GamePath").Text=String.IsNullOrEmpty(gameRoot)?"尚未选择游戏目录":gameRoot;Text("VersionState").Text=compatible?"版本匹配 · "+matchedGameVersion:"需要选择匹配版本的游戏";Text("ModPath").Text=store.Root;
  }
  void ChooseGame(){
   var dialog=new OpenFileDialog{Title="选择游戏目录中的 WorldApart.exe",Filter="不问凡尘|WorldApart.exe",CheckFileExists=true};
   if(Directory.Exists(gameRoot))dialog.InitialDirectory=gameRoot;if(dialog.ShowDialog(window)!=true)return;gameRoot=Path.GetDirectoryName(dialog.FileName);VerifyGame();
   Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));File.WriteAllText(settingsFile,Json.Write(new{gameRoot=gameRoot}),new UTF8Encoding(false));Refresh();
  }
  void Save(){
   if(!draft.TimeoutValid)throw new InvalidDataException("回复超时需要填写 30–1800 之间的整数。");
   VerifyGame();if(!compatible)throw new InvalidOperationException("游戏版本不匹配，未写入文件。");store.Save(Running());draft.Accept();
   message="已保存并启用。下次启动游戏生效。";Filter();FillNpc();FillPrompt();Refresh();
  }
  void Discard(){
   if(MessageBox.Show(window,"放弃全部未保存更改，并重新读取磁盘中的配置？","放弃更改",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   var reloaded=draft.ReloadSaved();draft=reloaded;store=reloaded.Store;loading=true;Get<TextBox>("TimeoutInput").Text=draft.TimeoutText;loading=false;Filter();FillNpc();FillPrompt();message="已重新读取保存的配置。";Refresh();
  }
  void Disable(){
   if(MessageBox.Show(window,"禁用后，超时、人物字段与全局提示词覆盖都会停止生效。\n文件及未保存更改会保留。继续？","禁用编辑器配置",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   store.Disable(Running());message="已禁用配置。下次启动游戏生效。";Refresh();
  }
  void OpenFolder(string folder){if(!Directory.Exists(folder))throw new DirectoryNotFoundException("此目录尚未创建，首次保存后会生成。");Process.Start(new ProcessStartInfo("explorer.exe","\""+folder+"\""){UseShellExecute=true});}
  void Export(){
   var dialog=new SaveFileDialog{Title="导出当前草稿",Filter="草稿 JSON|*.json",FileName="AI-编辑草稿-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".json"};
   if(dialog.ShowDialog(window)!=true)return;
   File.WriteAllText(dialog.FileName,Json.Write(new{Format="WorldApart.AI.Editor.Draft",Version=1,GameVersion=matchedGameVersion??catalog.Version,Changes=draft.Changes()}),new UTF8Encoding(false));message="草稿已导出："+Path.GetFileName(dialog.FileName);Refresh();
  }
  void Review(){
   var changes=draft.Changes();if(changes.Count==0)return;
   var dialog=new Window{Title="查看未保存更改",Owner=window,Width=850,Height=580,MinWidth=700,MinHeight=460,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White,FontFamily=window.FontFamily,FontSize=13,Resources=window.Resources};
   var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});dialog.Content=grid;
   var title=new TextBlock{Text=changes.Count+" 项更改 · 保存前检查",FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,15)};grid.Children.Add(title);
   var split=new Grid();split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});split.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(split,1);grid.Children.Add(split);
   var list=new ListBox{ItemsSource=changes,Margin=new Thickness(0,0,16,0),BorderThickness=new Thickness(1),BorderBrush=Brushes.LightGray};split.Children.Add(list);
   var comparison=new Grid();comparison.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});comparison.RowDefinitions.Add(new RowDefinition());comparison.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});comparison.RowDefinitions.Add(new RowDefinition());Grid.SetColumn(comparison,1);split.Children.Add(comparison);
   var beforeLabel=new TextBlock{Text="上次保存",Margin=new Thickness(0,0,0,8)};comparison.Children.Add(beforeLabel);
   var before=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};Grid.SetRow(before,1);comparison.Children.Add(before);
   var afterLabel=new TextBlock{Text="当前草稿",Margin=new Thickness(0,14,0,8)};Grid.SetRow(afterLabel,2);comparison.Children.Add(afterLabel);
   var after=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top};Grid.SetRow(after,3);comparison.Children.Add(after);
   list.SelectionChanged+=delegate{var change=list.SelectedItem as EditChange;if(change!=null){before.Text=change.Before;after.Text=change.After;}};
   var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};Grid.SetRow(actions,2);grid.Children.Add(actions);
   var locate=new Button{Content="返回此项编辑",Margin=new Thickness(0,0,10,0)};actions.Children.Add(locate);
   locate.Click+=delegate{var change=list.SelectedItem as EditChange;if(change==null)return;dialog.Close();Locate(change);};
   var close=new Button{Content="关闭",IsCancel=true};close.Click+=delegate{dialog.Close();};actions.Children.Add(close);list.SelectedIndex=0;dialog.ShowDialog();
  }
 }
 static class WpfProgram {
  [STAThread]static void Main(string[] args){
   try{
    Catalog c;using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("AIEditor.Catalog"),Encoding.UTF8))c=Json.Read<Catalog>(reader.ReadToEnd());
    if(args.Length==2&&args[0]=="--self-test"){Tests.Run(args[1],c);DraftTests.Run(args[1],c);ExtendedTests.Run(args[1],c);return;}
    if(args.Length==2&&args[0]=="--render-test"){new Workspace(c,true).RenderTests(args[1]);return;}
    new Workspace(c,args.Contains("--ui-test")).Run(args.Contains("--ui-test")&&args.Contains("--ui-test-min"));
   }catch(Exception e){
    if(args.Length==2&&(args[0]=="--self-test"||args[0]=="--render-test")){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"FAILED.txt"),e.ToString());Environment.ExitCode=1;}
    else MessageBox.Show(e.Message,"编辑器无法启动",MessageBoxButton.OK,MessageBoxImage.Error);
   }
  }
 }
}


