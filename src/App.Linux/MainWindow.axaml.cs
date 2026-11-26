using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace EncodeFlow.App.Linux;

public partial class MainWindow:Window {
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};

 public MainWindow() {
  InitializeComponent();
  timer.Tick+=(_,_)=>ViewModel?.Refresh();
  timer.Start();
  this.FindControl<Button>("AddFilesButton")!.Click+=async(_,_)=>{
   if(ViewModel==null)return;
   var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
    Title="TSファイルを選択",AllowMultiple=true,
    FileTypeFilter=[new FilePickerFileType("TSファイル"){Patterns=["*.ts","*.m2ts","*.mts"]}]
   });
   var paths=files.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
   if(paths.Count>0)ViewModel.AddPaths(paths);
  };
  AddHandler(DragDrop.DropEvent,(_,e)=>{
   var files=e.Data.GetFiles();
   if(files==null||ViewModel==null)return;
   var paths=files.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
   if(paths.Count>0)ViewModel.AddPaths(paths);
  });
  AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=DragDropEffects.Copy;});
 }

 MainViewModel? ViewModel=>DataContext as MainViewModel;
 void InitializeComponent()=>AvaloniaXamlLoader.Load(this);
}
