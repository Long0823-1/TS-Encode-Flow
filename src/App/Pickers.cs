using TsEncodeFlow;

namespace EncodeFlow.App;
static class Pickers {
 static nint Handle=>WinRT.Interop.WindowNative.GetWindowHandle(Application.Current!.Windows[0].Handler.PlatformView);
 sealed class DialogResult {public bool Cancelled {get;set;}public List<string> Paths {get;set;}=[];}
 static async Task<List<string>> Choose(string mode,string extensions,string name="") {
  string helper=Path.Combine(AppContext.BaseDirectory,"Tools","ShellDialogs.exe");
  if(!File.Exists(helper))throw new FileNotFoundException("ファイル選択ツールがありません。ZIP全体を展開してください。",helper);
  var result=await Processes.Run(helper,new[]{mode,Handle.ToInt64().ToString(),extensions,name},CancellationToken.None);
  if(result.ExitCode!=0)throw new IOException("Windowsのファイル選択画面を開けませんでした。\n"+result.Error);
  var selection=Store.Json.Deserialize<DialogResult>(result.Output);return selection.Cancelled?[]:selection.Paths;
 }
 public static Task<List<string>> Files(string extension=".ts")=>Choose("open",extension);
 public static Task<List<string>> Images()=>Choose("open",".png;.jpg;.jpeg;.bmp;.webp");
 public static async Task<string?> Folder()=>(await Choose("folder","" )).FirstOrDefault();
 public static async Task<string?> Save(string name,string extension)=>(await Choose("save",extension,name)).FirstOrDefault();
 public static void Open(string path)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});
}
