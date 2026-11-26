using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TsEncodeFlow;

namespace EncodeFlow.App.Linux;

// 解析・比較画面の処理経路（Windows版 EncodingLabView が呼ぶ Core API に対応）。
// 画面と検証用スモークの両方がこの経路を通る。
static class LabScreenLogic {
 public static Task<Metadata> ReadInfo(EngineClientLinux client,string input,Preset preset,CancellationToken token,Action<string> log)=>
  MetadataReader.Read(input,client.Settings,preset.ServiceId,token,log);

 public static async Task<(SignalReport Report,Metadata Metadata,string Folder)> Signal(EngineClientLinux client,string input,Preset preset,CancellationToken token,Action<string> log) {
  var metadata=await MetadataReader.Read(input,client.Settings,preset.ServiceId,token,log);
  var report=await SignalDiagnostics.Analyze(input,metadata,client.Settings,token,log);
  string folder=Path.Combine(client.Data,"Lab",Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);Store.Save(Path.Combine(folder,"signal.json"),report);
  return (report,metadata,folder);
 }

 public static async Task<List<EncodePart>> Cadence(EngineClientLinux client,string input,Preset preset,CancellationToken token,Action<string> log) {
  var s=client.Settings;var metadata=await MetadataReader.Read(input,s,preset.ServiceId,token,log);
  var p=Store.Clone(preset);p.Cadence=CadenceMode.MixedVfr;p.Fps="";
  var j=new Job {Input=input,Metadata=metadata,Preset=p};
  var parts=AdvancedEncoder.Parts(j);
  foreach(var part in parts)part.Mode=await AdvancedEncoder.Cadence(j,s,part,token,log);
  string folder=Path.Combine(client.Data,"Lab",Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);Store.Save(Path.Combine(folder,"cadence.json"),parts);
  return parts;
 }
}

// TS診断のタイムライン描画（Windows版 SignalTimeline の対応物）。
sealed class SignalTimeline:Control {
 readonly SignalReport report;readonly double duration;
 public SignalTimeline(SignalReport report,double duration) {this.report=report;this.duration=duration;Height=80;}
 public override void Render(DrawingContext context) {
  base.Render(context);
  var rect=new Rect(Bounds.Size);
  context.FillRectangle(new SolidColorBrush(Color.Parse("#D3E3FD")),rect,8);
  var blue=new Pen(new SolidColorBrush(Color.Parse("#185ABC")),1);
  var red=new Pen(new SolidColorBrush(Color.Parse("#B3261E")),1);
  foreach(var issue in report.Issues) {
   double x=Math.Clamp(issue.Seconds/Math.Max(1,duration),0,1)*rect.Width;
   context.DrawLine(issue.Kind.Contains("時刻")?blue:red,new Point(x,12),new Point(x,rect.Height-12));
  }
  var text=new FormattedText("0",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,11,Brushes.Black);
  context.DrawText(text,new Point(6,0));
  var tail=new FormattedText(Commercials.Time(duration),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,11,Brushes.Black);
  context.DrawText(tail,new Point(Math.Max(6,rect.Width-tail.Width-6),0));
 }
}

// 解析・比較画面（Windows版 EncodingLabView の対応物）。
sealed class LabScreen:UserControl {
 readonly EngineClientLinux client;readonly Action<string> notice;
 readonly TextBlock fileLabel=Ui.Text("TSを選択してください",14,muted:true),status=Ui.Text("準備ができています",13,muted:true);
 readonly TextBox seconds=Ui.Entry("30"),qualities=Ui.Entry("18,23,28");
 readonly ComboBox presetPicker=new(){MinWidth=420},sourcePicker=new(){MinWidth=420},taskPicker=new(){MinWidth=420};
 readonly StackPanel results=new(){Spacing=10};
 readonly List<Preset> choices;List<Job> queued=[];
 CancellationTokenSource? cancellation;string input="";Metadata? metadata;bool busy;

 public LabScreen(EngineClientLinux client,Action<string> notice) {
  this.client=client;this.notice=notice;
  choices=client.Settings.Presets.ToList();
  presetPicker.ItemsSource=choices.Select(p=>PresetCatalog.EngineName(p.Engine)+" · "+p.Name).ToList();
  presetPicker.SelectedIndex=Math.Max(0,choices.IndexOf(client.Selected));
  queued=client.Jobs.Where(j=>File.Exists(j.Input)).DistinctBy(j=>j.Input).ToList();
  sourcePicker.ItemsSource=queued.Select(j=>j.Metadata?.Title??Path.GetFileName(j.Input)).ToList();
  sourcePicker.SelectionChanged+=(_,_)=>{if(busy||sourcePicker.SelectedIndex<0)return;input=queued[sourcePicker.SelectedIndex].Input;fileLabel.Text=input;metadata=null;};
  taskPicker.ItemsSource=new[]{"映像の比較プレビュー","品質・容量・速度の試算","TSドロップ・時刻飛びの診断","混在FPSの全編解析","登録済みジョブの結果・再開情報","入力情報・音声ストリーム一覧"};
  taskPicker.SelectedIndex=0;
  var execute=Ui.Button("選んだ解析を実行",async()=>await Execute(),bold:true);
  var header=Ui.Card(Ui.Stack(Ui.Text("変換前に、見て確かめる。",26,bold:true),
   Ui.Button("TSを選択",async()=>{if(busy)return;var selected=await PickTs();if(selected.Count==0)return;input=selected[0];fileLabel.Text=input;metadata=null;}),
   sourcePicker,fileLabel,presetPicker,
   Ui.Field("機能",taskPicker),Ui.Field("プレビュー時刻 秒",seconds),Ui.Field("試算する品質値（最大6種類）",qualities),
   execute,Ui.Button("解析を中止",()=>cancellation?.Cancel()),status));
  var root=new StackPanel {Spacing=16};root.Children.Add(header);root.Children.Add(results);
  Content=Ui.Scroll(root);
 }

 Preset ChosenPreset=>choices[Math.Max(0,presetPicker.SelectedIndex)];
 void Log(string message)=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>status.Text=message.Length>240?message[..240]:message);

 async Task Execute() {
  if(busy){notice("解析中です。完了を待つか、中止してください。");return;}
  busy=true;using var cts=new CancellationTokenSource();cancellation=cts;
  try {
   results.Children.Clear();
   if(taskPicker.SelectedIndex==4){ShowJobs();status.Text="解析が完了しました。";return;}
   if(string.IsNullOrEmpty(input))throw new ArgumentException("TSを選択してください。");
   var p=Store.Clone(ChosenPreset);var token=cts.Token;
   if(taskPicker.SelectedIndex==5) {
    metadata=await LabScreenLogic.ReadInfo(client,input,p,token,Log);
    results.Children.Add(Ui.Text(metadata.Width+" × "+metadata.Height+" / "+metadata.Fps.ToString("F3")+"fps / "+metadata.FieldOrder+"\n色："+metadata.ColorSpace+" / "+metadata.ColorPrimaries+" / "+metadata.ColorTransfer+" / "+metadata.ColorRange,16));
    foreach(var track in metadata.AudioTracks)results.Children.Add(Ui.Card(Ui.Stack(
     Ui.Text("音声ストリーム "+track.Index,18,bold:true),
     Ui.Text(track.Language+" / "+track.Codec+" / "+track.Channels+"ch / "+track.Layout+"\n"+track.Title+(track.Default?"　既定":"")+(track.Commentary?"　解説":"")),
     Ui.Text("この番号をプリセットの「音声ストリーム番号を個別指定」に入力できます。",13,muted:true))));
   }else if(taskPicker.SelectedIndex==0) {
    double time=ReadSeconds();string folder=Path.Combine(client.Data,"Lab",Guid.NewGuid().ToString("N"));
    var pair=await EncodingLab.Preview(input,p,client.Settings,time,folder,token,Log);ShowPreview(pair);
   }else if(taskPicker.SelectedIndex==1) {
    var qs=(qualities.Text??"").Split(',').Select(v=>int.TryParse(v.Trim(),out int q)?q:throw new ArgumentException("品質値はカンマ区切りの整数です。"));
    string folder=Path.Combine(client.Data,"Lab",Guid.NewGuid().ToString("N"));
    var trials=await EncodingLab.Trial(input,p,client.Settings,qs,folder,token,Log);
    foreach(var t in trials)results.Children.Add(Ui.Card(Ui.Stack(
     Ui.Text("品質 "+t.Quality,20,bold:true),
     Ui.Text("推定映像＋AAC容量 "+t.EstimatedMegabytes.ToString("F0")+" MiB / 映像処理 "+t.Speed.ToString("F2")+"倍速\n"+t.Metric+"："+(t.Ssim<0?"測定不可":t.Ssim.ToString("F5")),14),
     Ui.Button("サンプル映像を開く",()=>LinuxShell.OpenPath(t.File)),
     Ui.Button("この品質でカスタムプリセットを保存",()=>{var chosen=Store.Clone(p);chosen.Name=p.Name+"・試算Q"+t.Quality;chosen.Quality=t.Quality;chosen.RateMode=RateMode.Quality;client.SavePreset(chosen);status.Text="カスタムプリセットを保存して選択しました。";}))));
    results.Children.Add(Ui.Text("3区間×最大6秒の概算です。SSIMは同じフィルタ適用後の映像を基準にし、主観画質の優劣を保証しません。コピー音声・字幕・コンテナの容量や、全編の処理時間は異なります。",13,muted:true));
   }else if(taskPicker.SelectedIndex==2) {
    var (report,m,_) = await LabScreenLogic.Signal(client,input,p,token,Log);metadata=m;ShowSignal(report,m.Duration);
   }else {
    var parts=await LabScreenLogic.Cadence(client,input,p,token,Log);ShowParts(parts);
   }
   status.Text="解析が完了しました。";
  }catch(OperationCanceledException){status.Text="解析を中止しました。";}
  catch(Exception e){status.Text=e.Message;notice(e.Message);}
  finally{busy=false;cancellation=null;}
 }

 double ReadSeconds()=>double.TryParse(seconds.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)&&n>=0?n:throw new ArgumentException("時刻を0以上の秒数で入力してください。");

 void ShowPreview(PreviewPair pair) {
  var zoom=new Slider {Minimum=0.25,Maximum=4,Value=1};
  var zoomLabel=Ui.Text("倍率 100%",13,muted:true);
  var image1=new Image {Source=LogoBitmapImage.LoadFile(pair.Original),Stretch=Stretch.Uniform};
  var image2=new Image {Source=LogoBitmapImage.LoadFile(pair.Filtered),Stretch=Stretch.Uniform};
  (int W,int H) PngSize(string path){using var f=File.OpenRead(path);f.Position=16;byte[] b=new byte[8];f.ReadExactly(b);return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(0,4)),System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(4,4)));}
  void Resize(){foreach(var (img,path) in new[]{(image1,pair.Original),(image2,pair.Filtered)}){var (w,h)=PngSize(path);img.Width=w*zoom.Value;img.Height=h*zoom.Value;}zoomLabel.Text="倍率 "+(zoom.Value*100).ToString("F0")+"%";}
  zoom.ValueChanged+=(_,_)=>Resize();Resize();
  var grid=new Grid {ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=12};
  var left=Ui.Stack(Ui.Text("元映像",17,bold:true),new ScrollViewer {Content=image1,Height=360,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});
  var right=Ui.Stack(Ui.Text("フィルタ適用後",17,bold:true),new ScrollViewer {Content=image2,Height=360,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});
  Grid.SetColumn(left,0);Grid.SetColumn(right,1);grid.Children.Add(left);grid.Children.Add(right);
  results.Children.Add(Ui.Card(Ui.Stack(Ui.Text(pair.Seconds.ToString("F3")+"秒付近の比較",20,bold:true),
   Ui.Text("縞や細線を拡大して確認できます。逆テレシネではフレームを再構成するため、完全に同じ画像とは限りません。",13,muted:true),
   zoomLabel,zoom,grid,
   Ui.Row(Ui.Button("−1/60秒",()=>{seconds.Text=Math.Max(0,pair.Seconds-1001.0/60000).ToString("F6",CultureInfo.InvariantCulture);status.Text="時刻を変更しました。「選んだ解析を実行」で更新します。";}),
          Ui.Button("＋1/60秒",()=>{seconds.Text=(pair.Seconds+1001.0/60000).ToString("F6",CultureInfo.InvariantCulture);status.Text="時刻を変更しました。「選んだ解析を実行」で更新します。";})))));
 }

 public void ShowSignal(SignalReport report,double duration) {
  results.Children.Add(Ui.Card(Ui.Stack(Ui.Text("TS診断",22,bold:true),
   Ui.Text("パケット "+report.Packets+" / CC欠落候補 "+report.ContinuityErrors+" / TEI "+report.TransportErrors+" / 同期ずれ "+report.SyncErrors+" / 時刻飛び "+report.TimestampGaps),
   new SignalTimeline(report,duration),
   Ui.Text("横軸＝録画時間、赤＝欠落・受信エラー、青＝時刻飛び。PCR基準の時刻もPTSと完全一致するものではなく、PCRがない位置はファイル位置からの概算です。欠落は修復せず記録します。",13,muted:true))));
  ShowPage(report.Issues.Select(i=>(i.Approximate?"約 ":"")+Commercials.Time(i.Seconds)+"　"+i.Kind+"　"+i.Detail).ToList());
  if(report.Truncated)results.Children.Add(Ui.Text("詳細は最初の10,000件まで記録しています。総件数は上部に表示します。",13,muted:true));
 }

 // 検証用：信号診断のトランスポート解析だけを同期実行して結果を表示する（--demo-signal 用）。
 public void LoadSignalDemo(string file,double duration) {
  input=file;fileLabel.Text=file;
  var report=SignalDiagnostics.Transport(file,duration,CancellationToken.None);
  results.Children.Clear();ShowSignal(report,duration);
  status.Text="TS診断（トランスポート解析）を表示しています。";
 }

 void ShowParts(List<EncodePart> parts) {
  results.Children.Add(Ui.Text("区間ごとの処理方式",22,bold:true));
  ShowPage(parts.Select(p=>Commercials.Time(p.Start)+" ～ "+Commercials.Time(p.End)+"　"+(p.Mode=="film"?"23.976p":p.Mode=="video"?"フィールド倍速（59.94p等）":p.Mode=="progressive"?"入力プログレッシブ維持":"プリセット")+(p.Reused?"　再利用":"")+(p.Complete?"　検証済み":"")).ToList());
 }

 void ShowPage(List<string> text) {
  int page=0;var list=new StackPanel {Spacing=4};var label=Ui.Text("",13,muted:true);
  void Update(){list.Children.Clear();foreach(string line in text.Skip(page*100).Take(100))list.Children.Add(Ui.Text(line,13,muted:true));label.Text=text.Count+"件 / "+(page+1)+"ページ";}
  Update();
  results.Children.Add(Ui.Card(Ui.Stack(
   Ui.Row(Ui.Button("前の100件",()=>{page=Math.Max(0,page-1);Update();}),Ui.Button("次の100件",()=>{if((page+1)*100<text.Count)page++;Update();}),label),list)));
 }

 void ShowJobs() {
  client.Refresh();
  foreach(var j in client.Jobs.Where(j=>j.Advanced!=null||j.Signal!=null)) {
   var job=j;
   results.Children.Add(Ui.Card(Ui.Stack(
    Ui.Text(job.Metadata?.Title??Path.GetFileName(job.Input),20,bold:true),
    Ui.Text(job.Advanced==null?"":job.Advanced.Parts.Count(p=>p.Complete)+" / "+job.Advanced.Parts.Count+"区間を検証済み、再利用 "+job.Advanced.ReusedParts+"区間\n"+(job.State==State.Completed&&job.CacheCleanupWarning.Length==0&&!Directory.Exists(job.Advanced.CacheFolder)?"キャッシュは成功後に自動削除済み":job.Advanced.CacheFolder)+(job.CacheCleanupWarning.Length>0?"\n削除の警告："+job.CacheCleanupWarning:""),13,muted:true),
    Ui.Button("このジョブの解析結果を表示",()=>{results.Children.Clear();if(job.Advanced!=null)ShowParts(job.Advanced.Parts);if(job.Signal!=null)ShowSignal(job.Signal,job.Metadata?.Duration??1);}),
    Ui.Button("このジョブの再開キャッシュを削除",async()=>{
     if(!await new ConfirmWindow("再開キャッシュを削除","検証済みの映像区間を削除します。再試行時は映像を最初から処理します。元TS・完成動画・ログは保持します。","削除する","キャンセル").ShowFor(this))return;
     client.Send("clear-encode-cache",job.Id);status.Text="ワーカーにキャッシュ削除を依頼しました。";
    }))));
  }
  if(results.Children.Count==0)results.Children.Add(Ui.Text("詳細パイプラインやTS診断の結果があるジョブはまだありません。",14,muted:true));
 }

 async Task<IReadOnlyList<string>> PickTs() {
  var top=TopLevel.GetTopLevel(this);if(top==null)return [];
  var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
   Title="TSファイルを選択",AllowMultiple=false,
   FileTypeFilter=[new FilePickerFileType("TSファイル"){Patterns=["*.ts","*.m2ts","*.mts"]}]});
  return files.Select(f=>f.TryGetLocalPath()).Where(p=>!string.IsNullOrEmpty(p)).Select(p=>p!).ToList();
 }
}
