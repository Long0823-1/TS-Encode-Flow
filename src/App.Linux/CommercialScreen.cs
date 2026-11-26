using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// CM・タイムリスト画面の処理経路（Windows版 CommercialView の確定・読込に対応）。
// 画面と検証用スモークの両方がこの経路を通る。
static class CommercialScreenLogic {
 public static List<Job> Available(EngineClientLinux client) {
  client.Refresh();
  return client.Jobs.Where(j=>j.Commercials!=null).OrderByDescending(j=>j.Created).ToList();
 }

 // Windows版と同じ：Build で検証してから Inbox へ approve-cm を送り、ワーカーを起こす。
 public static void Approve(EngineClientLinux client,Job job,List<TimeRange> cuts,CmMode mode) {
  var plan=job.Commercials??throw new InvalidOperationException("CM区間がありません。");
  Commercials.Build(cuts,plan.Duration,plan.Fps,plan.Fingerprint);
  Store.Save(Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json"),
   new QueueManager.Request {Action="approve-cm",JobId=job.Id,CmCuts=cuts,CmMode=mode,Fingerprint=plan.Fingerprint});
  client.Wake();
 }

 // 診断用のCM解析（Core の CmAnalysis をそのまま呼ぶ）。結果は表示専用で、変換時の解析はワーカーが行う。
 public static Task<CmPlan> Analyze(EngineClientLinux client,Job job,CancellationToken token,Action<string> log) {
  var clone=Store.Clone(job);
  return CmAnalysis.Analyze(clone,client.Settings,client.Data,token,log,()=>{});
 }

 // CM解析ツールの設定状態（正直な状態表示用）。
 public static string ToolStatus(EngineClientLinux client) {
  var s=client.Settings;var missing=new List<string>();
  if(!File.Exists(s.ChapterExe))missing.Add("chapter_exe");
  if(!File.Exists(s.LogoFrame))missing.Add("logoframe");
  return missing.Count==0?"CM解析ツールは設定されています。":"CM解析ツールが未設定／見つかりません（"+string.Join("、",missing)+"）。設定画面の「変換ツール」でパスを指定してください。Linux では再ビルド版のCMツールが必要です（本家ツールの Linux 再ビルドは cm-delogo ブランチで用意します）。";
 }
}

// CM区間のバー表示（青＝本編、橙＝CM）。Windows版の星幅グリッドと同じ見た目を描画で再現する。
sealed class SegmentBar:Control {
 public CmPlan? Plan {get;set;}
 static readonly IBrush MainBrush=new SolidColorBrush(Color.Parse("#185ABC"));
 static readonly IBrush CmBrush=new SolidColorBrush(Color.Parse("#D47719"));
 public SegmentBar()=>Height=18;
 public override void Render(DrawingContext context) {
  base.Render(context);
  var plan=Plan;if(plan==null||plan.Duration<=0)return;
  double total=plan.Segments.Sum(s=>s.End-s.Start);if(total<=0)return;
  double x=0;double gap=plan.Segments.Count>1?2:0;
  foreach(var segment in plan.Segments) {
   double w=Math.Max(1,(segment.End-segment.Start)/total*(Bounds.Width-gap*(plan.Segments.Count-1)));
   context.FillRectangle(segment.IsCommercial?CmBrush:MainBrush,new Rect(x,0,w,Bounds.Height));
   x+=w+gap;
  }
 }
}

// CM・タイムリスト画面（Windows版 CommercialView の対応物）。
sealed class CommercialScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly ComboBox jobs=new(){MinWidth=420};
 readonly StackPanel content=new(){Spacing=16},ranges=new(){Spacing=12};
 readonly List<(TextBox Start,TextBox End,Control View)> rows=[];
 List<Job> available=[];Job? job;
 CancellationTokenSource? analysisCancel;bool analyzing;

 public CommercialScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;
  jobs.SelectionChanged+=(_,_)=>Show();
  var header=Ui.Card(Ui.Stack(
   Ui.Text("CMを切る。区間だけ残す。",27,bold:true),
   Ui.Text("CM候補を一覧で確認し、必要なら時刻を修正してください。\n本編を誤って削らないよう、最初は確認してから確定する設定をおすすめします。",14,muted:true),
   Ui.Button("一覧を更新",Reload),jobs));
  var root=new StackPanel {Spacing=16};root.Children.Add(header);root.Children.Add(content);
  Content=Ui.Scroll(root);
  Reload();
 }

 public void Reload() {
  available=CommercialScreenLogic.Available(client);
  jobs.ItemsSource=available.Select(j=>(j.State==State.NeedsCmReview?"確認待ち · ":"")+(j.Metadata?.Title??Path.GetFileName(j.Input))).ToList();
  jobs.SelectedIndex=available.Count>0?0:-1;
  Show();
 }

 void AddRange(double start,double end,bool readOnly) {
  var a=Ui.Entry(Commercials.Time(start));var z=Ui.Entry(Commercials.Time(end));
  a.IsReadOnly=z.IsReadOnly=readOnly;
  var grid=new Grid {ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=12};
  var fa=Ui.Field("開始 hh:mm:ss.fff",a);var fz=Ui.Field("終了 hh:mm:ss.fff",z);
  Grid.SetColumn(fa,0);Grid.SetColumn(fz,1);grid.Children.Add(fa);grid.Children.Add(fz);
  var pill=new Border {Background=new SolidColorBrush(Color.Parse("#FFE5C2")),CornerRadius=new CornerRadius(8),Padding=new Thickness(8,2,8,2),Child=Ui.Text("CM区間",11),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left};
  var stack=Ui.Stack(pill,grid);
  var card=Ui.Card(stack);var row=(a,z,(Control)card);rows.Add(row);
  if(!readOnly)stack.Children.Add(Ui.Button("このCM区間を削除",()=>{rows.Remove(row);ranges.Children.Remove(card);}));
  ranges.Children.Add(card);
 }

 void Show() {
  content.Children.Clear();ranges.Children.Clear();rows.Clear();
  job=jobs.SelectedIndex>=0&&jobs.SelectedIndex<available.Count?available[jobs.SelectedIndex]:null;
  // CM解析（診断）のカードは、区間の有無にかかわらず対象ジョブがあれば出す
  content.Children.Add(AnalysisCard());
  if(job?.Commercials==null) {
   content.Children.Add(Ui.Text("CM処理を有効にしたプリセットでTSを追加すると、ここに区間を表示します。",16,muted:true));
   return;
  }
  var selected=job;var plan=selected.Commercials;bool readOnly=selected.State is State.Completed or State.Running or State.Waiting;
  content.Children.Add(Ui.Card(Ui.Stack(
   Ui.Text(selected.Metadata.Title,21,bold:true),
   Ui.Text("入力時間 "+Commercials.Time(plan.Duration)+" · "+plan.Source,13,muted:true),
   Ui.Text(readOnly?"処理中・完了済みの区間です。修正する場合はTSを新しく追加してください。":"候補を確認し、CMとして扱う開始・終了時刻を入力してください。",13,muted:true))));
  content.Children.Add(new SegmentBar {Plan=plan});
  content.Children.Add(Ui.Text("青＝本編　橙＝CM候補（確定後はCM）",12,muted:true));
  var timeline=Ui.Stack(Ui.Text("タイムリスト",20,bold:true));
  foreach(var segment in plan.Segments) {
   var line=Ui.Text(Commercials.Time(segment.Start)+" → "+Commercials.Time(segment.End)+"　"+(segment.IsCommercial?(plan.Reviewed?"CM":"CM候補"):"本編"),14);
   if(segment.IsCommercial)line.Foreground=new SolidColorBrush(Color.Parse("#9D570F"));
   timeline.Children.Add(line);
  }
  content.Children.Add(Ui.Card(timeline));
  var mode=Ui.Picker(new[]{"CMを残して、本編／CMチャプターを付ける","CMをカットして、本編をつなぐ"},Math.Max(0,(int)selected.Preset.CmMode-1));
  mode.IsEnabled=!readOnly;
  content.Children.Add(Ui.Field("出力方法",mode,"CMカット時は音声をAACへ再変換し、FPS指定を使わず入力のフレームレートで処理します。"));
  foreach(var segment in plan.Segments.Where(x=>x.IsCommercial))AddRange(segment.Start,segment.End,readOnly);
  content.Children.Add(ranges);
  if(!readOnly) {
   content.Children.Add(Ui.Button("＋ CM区間を追加",()=>AddRange(0,Math.Min(15,plan.Duration),false)));
   content.Children.Add(Ui.Button("EDL / Trim AVSから読み込む",async() => {
    try {
     var files=await PickEdl();if(files.Count==0)return;
     var imported=Commercials.Import(files[0],selected.Metadata,selected.Preset,plan.Fingerprint);
     rows.Clear();ranges.Children.Clear();
     foreach(var r in imported.Segments.Where(x=>x.IsCommercial))AddRange(r.Start,r.End,false);
     notice("区間を読み込みました。内容を確認して確定してください。");
    }catch(Exception e){notice(e.Message);}
   }));
   content.Children.Add(Ui.Button("この区間・出力方法で確定して続行",() => {
    try {
     var cuts=rows.Select(r=>new TimeRange {Start=Commercials.ParseTime(r.Start.Text??""),End=Commercials.ParseTime(r.End.Text??"")}).ToList();
     CommercialScreenLogic.Approve(client,selected,cuts,(CmMode)(mode.SelectedIndex+1));
     notice("CM区間の確定を送信しました。変換キューで状態を確認できます。");
    }catch(Exception e){notice(e.Message);}
   },bold:true));
  }
  content.Children.Add(Ui.Button("元のTSを再生ソフトで開く",()=>LinuxShell.OpenPath(selected.Input)));
  if(!string.IsNullOrEmpty(selected.Output)&&File.Exists(selected.Output+".cm.csv"))
   content.Children.Add(Ui.Button("区間CSVを開く",()=>LinuxShell.OpenPath(selected.Output+".cm.csv")));
 }

 // CM解析（診断）カード：Core の CmAnalysis を直接実行し、結果区間を表示する。
 // ツール未設定の場合は例外メッセージと設定状態をそのまま表示する（無言で代替しない）。
 Control AnalysisCard() {
  var status=Ui.Text(CommercialScreenLogic.ToolStatus(client),12,muted:true);
  var log=Ui.Text("",12,muted:true);
  var resultStack=new StackPanel {Spacing=6};
  var target=job??client.Jobs.OrderByDescending(j=>j.Created).FirstOrDefault();
  var run=Ui.Button("CM解析を試す（診断）",async() => {
   if(analyzing)return;
   if(target==null){notice("対象のジョブがありません。先にTSを追加してください。");return;}
   analyzing=true;resultStack.Children.Clear();
   using var cts=new CancellationTokenSource();analysisCancel=cts;
   try {
    log.Text="CM解析を実行中…";
    var plan=await CommercialScreenLogic.Analyze(client,target,cts.Token,m=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>log.Text=m.Length>240?m[..240]:m));
    log.Text="解析が完了しました（診断結果。変換時の解析はワーカーが改めて行います）。";
    resultStack.Children.Add(Ui.Text("入力時間 "+Commercials.Time(plan.Duration)+" · "+plan.Source,13,muted:true));
    resultStack.Children.Add(new SegmentBar {Plan=plan});
    foreach(var segment in plan.Segments)
     resultStack.Children.Add(Ui.Text(Commercials.Time(segment.Start)+" → "+Commercials.Time(segment.End)+"　"+(segment.IsCommercial?"CM候補":"本編"),13,muted:true));
   }catch(OperationCanceledException){log.Text="CM解析を中止しました。";}
   catch(Exception e){log.Text="CM解析を実行できません："+e.Message;}
   finally{analyzing=false;analysisCancel=null;}
  },bold:true);
  var cancel=Ui.Button("解析を中止",()=>analysisCancel?.Cancel());
  return Ui.Card(Ui.Stack(Ui.Text("CM解析（診断）",20,bold:true),
   Ui.Text("選択中のジョブに対して Core のCM解析をその場で実行し、検出区間を表示します。\n実際の変換ではワーカーが同じ解析を行い、確認が必要な場合は「CM確認待ち」で止まります。",13,muted:true),
   status,Ui.Row(run,cancel),log,resultStack));
 }

 async Task<IReadOnlyList<string>> PickEdl() {
  var top=TopLevel.GetTopLevel(this);if(top==null)return [];
  var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
   Title="EDL / Trim AVSを選択",AllowMultiple=false,
   FileTypeFilter=[new FilePickerFileType("CM区間ファイル"){Patterns=["*.edl","*.avs"]}]});
  return files.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
 }
}
