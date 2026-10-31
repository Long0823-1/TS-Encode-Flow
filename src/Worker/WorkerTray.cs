using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using TsEncodeFlow;

sealed class WorkerTray:ApplicationContext {
 readonly NotifyIcon icon;
 readonly System.Windows.Forms.Timer timer=new(){Interval=1000};
 readonly CancellationTokenSource stop=new();
 readonly Task<int> worker;
 readonly ToolStripMenuItem state=new("起動中…"){Enabled=false},exit=new("バックグラウンドを終了");
 readonly string data;
 string lastPending="";
 DateTime lastLaunch=DateTime.MinValue;
 public int Result {get;private set;}
 public WorkerTray(string folder,Func<CancellationToken,int> run) {
  data=folder;string gui=GuiPath;Icon drawing=File.Exists(gui)?Icon.ExtractAssociatedIcon(gui):null;
  var menu=new ContextMenuStrip();menu.Items.Add("GUIを開く",null,async(s,e)=>await OpenGui("ダッシュボード"));menu.Items.Add("変換キューを開く",null,async(s,e)=>await OpenGui("変換キュー"));menu.Items.Add("字幕・DRCSを開く",null,async(s,e)=>await OpenGui("字幕・DRCS"));menu.Items.Add(new ToolStripSeparator());menu.Items.Add(state);
  menu.Items.Add("処理を再開",null,(s,e)=>Integration.Send(data,"resume"));menu.Items.Add("新規開始を一時停止",null,(s,e)=>Integration.Send(data,"pause"));menu.Items.Add(new ToolStripSeparator());menu.Items.Add(exit);
  icon=new NotifyIcon {Icon=drawing??SystemIcons.Application,Text="TS Encode Flow · 起動中",ContextMenuStrip=menu,Visible=true};
  icon.DoubleClick+=async(s,e)=>await OpenGui("変換キュー");icon.BalloonTipClicked+=async(s,e)=>await OpenGui("字幕・DRCS");
  exit.Click+=(s,e)=>{
   var status=Integration.ReadStatus(data);
   if(status.Active>0&&MessageBox.Show("実行中の変換を中止してバックグラウンドを終了しますか？ 元TSは保持します。","TS Encode Flow",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
   exit.Enabled=false;state.Text="終了処理中…";stop.Cancel();
  };
  worker=Task.Run(()=>run(stop.Token));timer.Tick+=(s,e)=>Refresh();timer.Start();
 }
 string GuiPath=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","TS-Encode-Flow.exe"));
 async Task OpenGui(string page) {
  try {
   if(await GuiBridge.TryOpen(data,page))return;
   if((DateTime.UtcNow-lastLaunch).TotalSeconds<5)return;
   if(!File.Exists(GuiPath))throw new FileNotFoundException("GUIがありません。配布ZIP全体を展開してください。",GuiPath);
   lastLaunch=DateTime.UtcNow;
   Process.Start(new ProcessStartInfo(GuiPath,"--data "+Processes.Quote(data)+" --page "+Processes.Quote(page)){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(GuiPath)});
  }catch(Exception e){MessageBox.Show(e.Message,"GUIを開けませんでした",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 }
 void Refresh() {
  if(worker.IsCompleted){Result=worker.IsCompletedSuccessfully?worker.Result:1;ExitThread();return;}
  if(stop.IsCancellationRequested)return;
  try {
   var status=Integration.ReadStatus(data);var jobs=Integration.ReadJobs(data);
   var pending=jobs.Where(j=>j.State==State.NeedsInput).ToList();int waiting=jobs.Count(j=>j.State==State.Waiting);
   string text=status.Paused?"一時停止中":status.Active>0?status.Active+"本を変換中":pending.Count>0?"DRCS入力待ち "+pending.Count+"本":waiting>0?"録画終了待ち "+waiting+"本":"待機中";
   state.Text=text;icon.Text="TS Encode Flow · "+text;
   string ids=string.Join(",",pending.Select(j=>j.Id).OrderBy(x=>x));
   if(ids.Length>0&&ids!=lastPending){icon.ShowBalloonTip(8000,"字幕・DRCSの確認が必要です",pending.Count+"本が入力待ちです。クリックすると補正画面を開きます。",ToolTipIcon.Warning);}lastPending=ids;
  }catch(IOException){state.Text="状態を読み直しています…";}
 }
 protected override void Dispose(bool disposing) {
  if(disposing){timer.Stop();timer.Dispose();icon.Visible=false;icon.ContextMenuStrip?.Dispose();icon.Icon?.Dispose();icon.Dispose();stop.Dispose();}base.Dispose(disposing);
 }
}
