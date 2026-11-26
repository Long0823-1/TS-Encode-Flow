using System.Diagnostics;
using Avalonia.Controls;

namespace EncodeFlow.App.Linux;

// Linux での「開く」操作と確認ダイアログ（Windows版 Pickers.Open / DisplayAlert の対応物）。
static class LinuxShell {
 // ファイル・フォルダーを既定のアプリで開く（xdg-open）。失敗しても例外にしない。
 public static void OpenPath(string path) {
  try{Process.Start(new ProcessStartInfo("xdg-open",TsEncodeFlow.Processes.Quote(path)){UseShellExecute=false});}
  catch{/* 開けない場合は何もしない（画面表示側の責務ではない） */}
 }
}

// 確認ダイアログ（Windows版 DisplayAlert の対応物）。OK のときだけ true を返す。
sealed class ConfirmWindow:Window {
 readonly TaskCompletionSource<bool> done=new();
 public ConfirmWindow(string title,string message,string ok,string cancel="戻る") {
  Title=title;Width=460;SizeToContent=SizeToContent.Height;CanResize=false;
  var okButton=new Button {Content=ok,FontWeight=Avalonia.Media.FontWeight.Bold};
  var cancelButton=new Button {Content=cancel};
  okButton.Click+=(_,_)=>{done.TrySetResult(true);Close();};
  cancelButton.Click+=(_,_)=>{done.TrySetResult(false);Close();};
  Closed+=(_,_)=>done.TrySetResult(false);
  Content=new StackPanel {Margin=new Avalonia.Thickness(20),Spacing=16,Children={
   Ui.Text(message,14),
   Ui.Row(cancelButton,okButton)}};
 }
 public async Task<bool> ShowFor(Control owner) {
  var top=TopLevel.GetTopLevel(owner) as Window;
  if(top==null)return false;
  Show(top);
  return await done.Task;
 }
}
