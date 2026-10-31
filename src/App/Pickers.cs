using Windows.Storage.Pickers;
using TsEncodeFlow;

namespace EncodeFlow.App;
static class Pickers {
 static nint Handle=>WinRT.Interop.WindowNative.GetWindowHandle(Application.Current!.Windows[0].Handler.PlatformView);
 public static async Task<List<string>> Files(string extension=".ts") {
  var picker=new FileOpenPicker();picker.FileTypeFilter.Add(extension);WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);
  var files=await picker.PickMultipleFilesAsync();return files.Select(f=>f.Path).ToList();
 }
 public static async Task<string?> Folder() {
  var picker=new FolderPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);return (await picker.PickSingleFolderAsync())?.Path;
 }
 public static async Task<List<string>> Images() {
  var picker=new FileOpenPicker();foreach(var ext in new[]{".png",".jpg",".jpeg",".bmp",".webp"})picker.FileTypeFilter.Add(ext);WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);
  var files=await picker.PickMultipleFilesAsync();return files.Select(f=>f.Path).ToList();
 }
 public static async Task<string?> Save(string name,string extension) {
  var picker=new FileSavePicker {SuggestedFileName=name};picker.FileTypeChoices.Add(extension==".bat"?"録画後実行BAT":"プリセット",new List<string>{extension});WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);return (await picker.PickSaveFileAsync())?.Path;
 }
 public static void Open(string path)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});
}
