using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace WorldApartAI {
 public sealed partial class Workspace {
  void RenderProof(string folder,string name){
   window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));window.UpdateLayout();window.Dispatcher.Invoke(DispatcherPriority.Render,new Action(delegate{}));
   var visual=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(folder,name+".png")))encoder.Save(stream);
  }
  static System.Collections.Generic.IEnumerable<T> VisualChildren<T>(DependencyObject parent)where T:DependencyObject {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)yield return (T)child;foreach(var nested in VisualChildren<T>(child))yield return nested;}}
  public void RenderTests(string folder){
   Directory.CreateDirectory(folder);window.Show();
   Get<RadioButton>("NavRequests").IsChecked=true;RenderProof(folder,"00-requests");
   Get<RadioButton>("NavNpc").IsChecked=true;RenderProof(folder,"01-gallery");
   Get<TextBox>("GallerySearch").Text="苏倾盏";if(filteredRows.Count<2)throw new Exception("Same-name search missing distinct identities");RenderProof(folder,"02-same-name");
   var npc=rows.First(r=>r.Npc.Id==100000);window.UpdateLayout();var card=VisualChildren<Button>(Get<ItemsControl>("GalleryItems")).First(b=>b.Tag==npc);card.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent,card));if(galleryVisible||current.Id!=100000)throw new Exception("Card selection routing failed: gallery="+galleryVisible+", current="+(current==null?-1:current.Id)+", tag="+card.Tag);RenderProof(folder,"03-npc-editor");
   Get<ComboBox>("NpcField").SelectedItem=Get<ComboBox>("NpcField").Items.Cast<FieldChoice>().First(c=>c.Key=="hiddenPersona");Get<TextBox>("StyleEditor").Text="仅用于界面测试的草稿。";if(!draft.Changes().Any(c=>c.FieldKey==Store.NpcKey(100000,"hiddenPersona","zh-Hans")))throw new Exception("Field editing failed");
   SetGallery(true);if(Get<TextBox>("GallerySearch").Text!="苏倾盏"||!rows.First(r=>r.Npc.Id==100000).Badge.Contains("草稿"))throw new Exception("Return loses search or draft badge");RenderProof(folder,"04-draft-card");
   Get<TextBox>("GallerySearch").Text="no-person-should-match";if(filteredRows.Count!=0)throw new Exception("Empty search failed");RenderProof(folder,"05-empty");
   Get<TextBox>("GallerySearch").Text="";galleryPage=2;RenderGallery();SetGallery(false);SetGallery(true);if(galleryPage!=2)throw new Exception("Return loses page");
   window.Width=980;window.Height=660;galleryPage=0;RenderGallery();RenderProof(folder,"06-gallery-min");if(Text("GalleryPageNumber").Text!="1 / 97"||Text("GalleryPageNumber").ActualWidth<56)throw new Exception("Page counter truncated");Get<ScrollViewer>("GalleryScroll").ScrollToVerticalOffset(200);window.UpdateLayout();SetGallery(false);SetGallery(true);window.UpdateLayout();if(Get<ScrollViewer>("GalleryScroll").VerticalOffset<190)throw new Exception("Return loses gallery scroll");
   Get<TextBox>("NpcSearch").Text="100000";Get<ListBox>("NpcList").SelectedItem=npc;SetGallery(false);RenderProof(folder,"07-editor-min");Get<Expander>("OriginalExpander").IsExpanded=true;RenderProof(folder,"10-editor-original-min");if(Get<TextBox>("StyleEditor").ActualHeight<90||Get<TextBox>("OriginalStyle").ActualHeight<45)throw new Exception("Original comparison too small");Get<Expander>("OriginalExpander").IsExpanded=false;
   Get<RadioButton>("NavPrompts").IsChecked=true;Get<ComboBox>("PromptSelect").SelectedItem=catalog.Prompts.First(p=>p.Key=="format.chat");RenderProof(folder,"08-prompt-min");Get<Expander>("PromptOriginalExpander").IsExpanded=true;RenderProof(folder,"11-prompt-original-min");if(Get<TextBox>("PromptEditor").ActualHeight<80)throw new Exception("Prompt comparison too small");Get<Expander>("PromptOriginalExpander").IsExpanded=false;
   Get<RadioButton>("NavSettings").IsChecked=true;RenderProof(folder,"09-settings-min");
   Get<RadioButton>("NavRequests").IsChecked=true;RenderProof(folder,"12-requests-min");
   Get<RadioButton>("NavPrompts").IsChecked=true;Get<ComboBox>("PromptSelect").SelectedItem=catalog.Prompts.First(p=>p.Key=="state.chat_relation_actions");RenderProof(folder,"13-relation-prompt-min");
   // No Save(), registry writes, dialogs, OS input, or game process interaction.
   timer.Stop();draft.Accept();window.Close();File.WriteAllText(Path.Combine(folder,"UI-PASSED.txt"),"PASS: rendered real WPF views at standard/minimum size; same-name search; field draft routing; card draft badge; return preserves search/page/scroll; card routed click; complete page counter; original comparisons at minimum size; empty search. No real game writes or external UI automation.");
  }
 }
}


