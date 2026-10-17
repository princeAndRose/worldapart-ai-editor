using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WorldApartAI {
 public sealed partial class Store {
  public bool PatchChangedOnDisk(){return Files.Any(f=>Fingerprint(f)!=fingerprints[f]);}
  public DateTime? LastPatchWrite(){
   var paths=Files.Select(PathFor).Where(File.Exists).ToArray();
   return paths.Length==0?(DateTime?)null:paths.Max(p=>File.GetLastWriteTimeUtc(p));
  }
 }
 public sealed partial class Workspace {
  string EffectReport(){
   var lines=new List<string>();
   lines.Add("检查范围：本地草稿、补丁文件和启用记录。不能据此确认游戏已加载或模型会遵循提示词。");
   lines.Add("");lines.Add("当前配置");
   lines.Add("游戏版本："+(compatible?matchedGameVersion:"未匹配，请选择正确的游戏目录"));
   lines.Add("游戏补丁草稿："+draft.Changes().Count+" 项未保存修改");
   lines.Add("启用记录："+(prefError!=null?"无法读取":enabled?"已启用（不代表游戏加载成功）":"未启用，请在退出游戏后保存游戏补丁"));
   bool changed=store.PatchChangedOnDisk();
   lines.Add("磁盘文件："+(changed?"打开编辑器后被其他程序改动。先导出预设，再重新打开编辑器检查。":"与编辑器读取时一致"));
   DateTime? modified=store.LastPatchWrite();
   lines.Add("补丁文件最近写入："+(modified.HasValue?modified.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"尚无补丁文件"));
   var processes=Process.GetProcessesByName("WorldApart");
   try{
    if(processes.Length==0)lines.Add("游戏进程：未运行。保存后启动游戏。");
    else {
     try{DateTime start=processes.Min(p=>p.StartTime.ToUniversalTime());lines.Add("游戏启动："+start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));lines.Add(modified.HasValue&&start<modified.Value?"补丁在本次启动后改动过，请完整退出并重新启动游戏。":"进程时间未发现需要重启的迹象；这不是加载成功的证明。");}
     catch{lines.Add("游戏进程：运行中，但无法读取启动时间。保存补丁后请完整退出并重新启动游戏。");}
    }
   }finally{foreach(var p in processes)p.Dispose();}
   if(current!=null){lines.Add("");lines.Add("所选人物："+current.Name+" / "+current.Id);lines.Add(CoverageHelp(current));}
   lines.Add("");lines.Add("保存了，但对话没有变化");
   lines.Add("1. 确认修改了游戏正在使用的语言、人物配置 ID 和字段。游戏中的同名角色可能不是同一配置。");
   lines.Add("2. 完整退出游戏 → 保存游戏补丁 → 重新启动游戏。返回主菜单不会重新加载全部配置。");
   lines.Add("3. 旧话题、聊天记录和长期记忆可能保留旧设定。结束当前话题后，用新一轮普通闲聊核对；不需要为排查删除存档。");
   lines.Add("4. 使用请求工作台记录一轮请求，查找你修改的独特短句。未进入请求时先查配置加载和交谈模式；已经进入而回复未遵循时，再检查历史影响和模型表现。记录默认关闭，含私人聊天，分享前请检查。");
   lines.Add("");lines.Add("超时仍不一致");
   lines.Add("游戏自定义模型中大于 0 的超时值优先于表补丁。代理等待时间与游戏超时分别设置，先到期的一方会结束等待。");
   lines.Add("");lines.Add("找不到人物 / 游戏内工坊提示无法读取");
   lines.Add("目录来自编辑器所匹配游戏版本的基础表；存档改名、动态生成名称和其他 Mod 新增角色不能仅凭名字定位。反馈时请附角色原名、称号、所在地点和游戏版本。");
   lines.Add("本作品含游戏外编辑器。请从工坊下载目录手动运行 WorldApart.AI.Editor.exe，再由编辑器保存补丁；不要把游戏内工坊对程序的提示当成编辑器已运行。仍无法启动时，请保留具体错误文字。");
   lines.Add("");lines.Add("与其他 Mod 共用");
   lines.Add("语音或其他 Mod 是否兼容，要看是否改动同一张表、同一字段或请求接口。尚未逐个验证，请附 Mod 名称与链接再核对。");
   return String.Join("\n",lines);
  }
  Window EffectDialog(){
   var dialog=new Window{Owner=window,Title="检查生效",Width=740,Height=570,MinWidth=540,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Resources=window.Resources,FontFamily=window.FontFamily};
   var grid=new Grid{Margin=new Thickness(22)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});dialog.Content=grid;
   var text=new TextBox{Text=EffectReport(),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};grid.Children.Add(text);
   var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};Grid.SetRow(buttons,1);grid.Children.Add(buttons);
   var refresh=new Button{Content="重新检查",Margin=new Thickness(0,0,12,0)};refresh.Click+=delegate{Guard(delegate{VerifyGame();Refresh();text.Text=EffectReport();});};buttons.Children.Add(refresh);
   var close=new Button{Content="关闭",IsCancel=true};close.Click+=delegate{dialog.Close();};buttons.Children.Add(close);return dialog;
  }
  void CheckEffect(){VerifyGame();Refresh();EffectDialog().ShowDialog();}
 }
}
