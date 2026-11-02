using System.Globalization;
using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class EncodingLabView:ContentView {
 readonly EngineClient client;
 readonly Label file=Pixel.Text("TSを選択してください",14,Pixel.Muted),status=Pixel.Text("準備ができています",13,Pixel.Muted);
 readonly Entry seconds=Pixel.Entry("30"),qualities=Pixel.Entry("18,23,28");
 readonly Picker preset=new(){Title="比較するプリセット"};
 readonly VerticalStackLayout results=Pixel.Stack();
 readonly List<Preset> choices;
 CancellationTokenSource? cancellation;
 string input="";
 Metadata? metadata;
 bool busy;
 public EncodingLabView(EngineClient c) {
  client=c;choices=c.Settings.Presets.ToList();preset.ItemsSource=choices.Select(p=>PresetCatalog.EngineName(p.Engine)+" · "+p.Name).ToList();preset.SelectedIndex=Math.Max(0,choices.IndexOf(c.Selected));
  var queued=c.Jobs.Where(j=>File.Exists(j.Input)).DistinctBy(j=>j.Input).ToList();var source=new Picker {Title="キューのTSから選択",ItemsSource=queued.Select(j=>j.Metadata?.Title??Path.GetFileName(j.Input)).ToList()};source.SelectedIndexChanged+=(s,e)=>{if(busy||source.SelectedIndex<0)return;input=queued[source.SelectedIndex].Input;file.Text=input;metadata=null;};
  var tasks=new Picker {ItemsSource=new[]{"映像の比較プレビュー","品質・容量・速度の試算","TSドロップ・時刻飛びの診断","混在FPSの全編解析","登録済みジョブの結果・再開情報","入力情報・音声ストリーム一覧"},SelectedIndex=0};
  var execute=Pixel.Button("選んだ解析を実行",async()=>await Run(async token=> {
   results.Children.Clear();if(tasks.SelectedIndex==4){ShowJobs();return;}
   if(string.IsNullOrEmpty(input))throw new ArgumentException("TSを選択してください。");
   var p=Store.Clone(choices[Math.Max(0,preset.SelectedIndex)]);var s=Store.Clone(c.Settings);string folder=Path.Combine(c.Data,"Lab",Guid.NewGuid().ToString("N"));
   if(tasks.SelectedIndex==5) {
    metadata=await MetadataReader.Read(input,s,p.ServiceId,token,Log);results.Children.Add(Pixel.Text(metadata.Width+" × "+metadata.Height+" / "+metadata.Fps.ToString("F3")+"fps / "+metadata.FieldOrder+"\n色："+metadata.ColorSpace+" / "+metadata.ColorPrimaries+" / "+metadata.ColorTransfer+" / "+metadata.ColorRange,16));
    foreach(var track in metadata.AudioTracks)results.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text("音声ストリーム "+track.Index,18,null,true),Pixel.Text(track.Language+" / "+track.Codec+" / "+track.Channels+"ch / "+track.Layout+"\n"+track.Title+(track.Default?"　既定":"")+(track.Commentary?"　解説":"")),Pixel.Text("この番号をプリセットの「音声ストリーム番号を個別指定」に入力できます。",13,Pixel.Muted))));
   }else if(tasks.SelectedIndex==0) {
    double time=ReadSeconds();var pair=await EncodingLab.Preview(input,p,s,time,folder,token,Log);ShowPreview(pair);
   }else if(tasks.SelectedIndex==1) {
    var qs=(qualities.Text??"").Split(',').Select(v=>int.TryParse(v.Trim(),out int q)?q:throw new ArgumentException("品質値はカンマ区切りの整数です。"));
    var trials=await EncodingLab.Trial(input,p,s,qs,folder,token,Log);
    foreach(var t in trials)results.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text("品質 "+t.Quality,20,null,true),Pixel.Text("推定映像＋AAC容量 "+t.EstimatedMegabytes.ToString("F0")+" MiB / 映像処理 "+t.Speed.ToString("F2")+"倍速\n"+t.Metric+"："+(t.Ssim<0?"測定不可":t.Ssim.ToString("F5")),14),Pixel.Button("サンプル映像を開く",()=>Pickers.Open(t.File)),Pixel.Button("この品質でカスタムプリセットを保存",()=>{var chosen=Store.Clone(p);chosen.Name=p.Name+"・試算Q"+t.Quality;chosen.Quality=t.Quality;chosen.RateMode=RateMode.Quality;c.SavePreset(chosen);status.Text="カスタムプリセットを保存して選択しました。";}))));
    results.Children.Add(Pixel.Text("3区間×最大6秒の概算です。SSIMは同じフィルタ適用後の映像を基準にし、主観画質の優劣を保証しません。コピー音声・字幕・コンテナの容量や、全編の処理時間は異なります。",13,Pixel.Muted));
   }else {
    metadata=await MetadataReader.Read(input,s,p.ServiceId,token,Log);
    if(tasks.SelectedIndex==2){var r=await SignalDiagnostics.Analyze(input,metadata,s,token,Log);Directory.CreateDirectory(folder);Store.Save(Path.Combine(folder,"signal.json"),r);ShowSignal(r,metadata.Duration);}
    else {p.Cadence=CadenceMode.MixedVfr;p.Fps="";var j=new Job {Input=input,Metadata=metadata,Preset=p};var parts=AdvancedEncoder.Parts(j);foreach(var part in parts){part.Mode=await AdvancedEncoder.Cadence(j,s,part,token,Log);}Directory.CreateDirectory(folder);Store.Save(Path.Combine(folder,"cadence.json"),parts);ShowParts(parts);}
   }
  }),true);
  Content=Pixel.Scroll(Pixel.Stack(Pixel.Card(Pixel.Stack(Pixel.Text("変換前に、見て確かめる。",26,null,true),Pixel.Button("TSを選択",async()=>{if(busy)return;var selected=await Pickers.Files();if(selected.Count==0)return;input=selected[0];file.Text=input;metadata=null;}),source,file,preset,Pixel.Field("機能",tasks),Pixel.Field("プレビュー時刻 秒",seconds),Pixel.Field("試算する品質値（最大6種類）",qualities),execute,Pixel.Button("解析を中止",()=>cancellation?.Cancel()),status)),results));
  Unloaded+=(s,e)=>cancellation?.Cancel();
 }
 double ReadSeconds()=>double.TryParse(seconds.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)&&n>=0?n:throw new ArgumentException("時刻を0以上の秒数で入力してください。");
 void Log(string message)=>Dispatcher.Dispatch(()=>status.Text=message.Length>240?message[..240]:message);
 async Task Run(Func<CancellationToken,Task> action) {
  if(busy)throw new InvalidOperationException("解析中です。完了を待つか、中止してください。");busy=true;using var cts=new CancellationTokenSource();cancellation=cts;
  try{await action(cts.Token);status.Text="解析が完了しました。";}catch(OperationCanceledException){status.Text="解析を中止しました。";}finally{busy=false;cancellation=null;}
 }
 void ShowPreview(PreviewPair pair) {
  var zoom=new Slider {Minimum=0.25,Maximum=4,Value=1};var zoomLabel=Pixel.Text("倍率 100%",13,Pixel.Muted);var image1=new Image {Source=ImageSource.FromFile(pair.Original),Aspect=Aspect.AspectFit};var image2=new Image {Source=ImageSource.FromFile(pair.Filtered),Aspect=Aspect.AspectFit};
  void Resize(){foreach(var item in new[]{(image1,pair.Original),(image2,pair.Filtered)}){using var f=File.OpenRead(item.Item2);f.Position=16;byte[] b=new byte[8];f.ReadExactly(b);int w=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(0,4)),h=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(4,4));item.Item1.WidthRequest=w*zoom.Value;item.Item1.HeightRequest=h*zoom.Value;}zoomLabel.Text="倍率 "+(zoom.Value*100).ToString("F0")+"%";}
  zoom.ValueChanged+=(s,e)=>Resize();Resize();var grid=new Grid {ColumnDefinitions=new(){new(GridLength.Star),new(GridLength.Star)},ColumnSpacing=12};
  grid.Add(Pixel.Stack(Pixel.Text("元映像",17,null,true),new ScrollView {Orientation=ScrollOrientation.Both,Content=image1,HeightRequest=360}),0,0);grid.Add(Pixel.Stack(Pixel.Text("フィルタ適用後",17,null,true),new ScrollView {Orientation=ScrollOrientation.Both,Content=image2,HeightRequest=360}),1,0);
  results.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text(pair.Seconds.ToString("F3")+"秒付近の比較",20,null,true),Pixel.Text("縞や細線を拡大して確認できます。逆テレシネではフレームを再構成するため、完全に同じ画像とは限りません。",13,Pixel.Muted),zoomLabel,zoom,grid,Pixel.Row(Pixel.Button("−1/60秒",()=>{seconds.Text=Math.Max(0,pair.Seconds-1001.0/60000).ToString("F6",CultureInfo.InvariantCulture);status.Text="時刻を変更しました。「選んだ解析を実行」で更新します。";}),Pixel.Button("＋1/60秒",()=>{seconds.Text=(pair.Seconds+1001.0/60000).ToString("F6",CultureInfo.InvariantCulture);status.Text="時刻を変更しました。「選んだ解析を実行」で更新します。";})))));
 }
 void ShowSignal(SignalReport report,double duration) {
  results.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text("TS診断",22,null,true),Pixel.Text("パケット "+report.Packets+" / CC欠落候補 "+report.ContinuityErrors+" / TEI "+report.TransportErrors+" / 同期ずれ "+report.SyncErrors+" / 時刻飛び "+report.TimestampGaps),new GraphicsView {Drawable=new SignalTimeline(report,duration),HeightRequest=80},Pixel.Text("横軸＝録画時間、赤＝欠落・受信エラー、青＝時刻飛び。PCR基準の時刻もPTSと完全一致するものではなく、PCRがない位置はファイル位置からの概算です。欠落は修復せず記録します。",13,Pixel.Muted))));
  ShowPage(report.Issues.Select(i=>(i.Approximate?"約 ":"")+Commercials.Time(i.Seconds)+"　"+i.Kind+"　"+i.Detail).ToList());
  if(report.Truncated)results.Children.Add(Pixel.Text("詳細は最初の10,000件まで記録しています。総件数は上部に表示します。",13,Pixel.Muted));
 }
 void ShowParts(List<EncodePart> parts) {results.Children.Add(Pixel.Text("区間ごとの処理方式",22,null,true));ShowPage(parts.Select(p=>Commercials.Time(p.Start)+" ～ "+Commercials.Time(p.End)+"　"+(p.Mode=="film"?"23.976p":p.Mode=="video"?"フィールド倍速（59.94p等）":p.Mode=="progressive"?"入力プログレッシブ維持":"プリセット")+(p.Reused?"　再利用":"")+(p.Complete?"　検証済み":"")).ToList());}
 void ShowPage(List<string> text) {
  int page=0;var list=Pixel.Stack();var label=Pixel.Text("",13,Pixel.Muted);void Update(){list.Children.Clear();foreach(string line in text.Skip(page*100).Take(100))list.Children.Add(Pixel.Text(line,13,Pixel.Muted));label.Text=text.Count+"件 / "+(page+1)+"ページ";}Update();
  results.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Row(Pixel.Button("前の100件",()=>{page=Math.Max(0,page-1);Update();}),Pixel.Button("次の100件",()=>{if((page+1)*100<text.Count)page++;Update();}),label),list)));
 }
 void ShowJobs() {
  client.Refresh();foreach(var j in client.Jobs.Where(j=>j.Advanced!=null||j.Signal!=null)) {
   results.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text(j.Metadata?.Title??Path.GetFileName(j.Input),20,null,true),Pixel.Text(j.Advanced==null?"":j.Advanced.Parts.Count(p=>p.Complete)+" / "+j.Advanced.Parts.Count+"区間を検証済み、再利用 "+j.Advanced.ReusedParts+"区間\n"+j.Advanced.CacheFolder,13,Pixel.Muted),Pixel.Button("このジョブの解析結果を表示",()=>{results.Children.Clear();if(j.Advanced!=null)ShowParts(j.Advanced.Parts);if(j.Signal!=null)ShowSignal(j.Signal,j.Metadata?.Duration??1);}),Pixel.Button("このジョブの再開キャッシュを削除",async()=>{var page=Application.Current!.Windows[0].Page!;if(!await page.DisplayAlert("再開キャッシュを削除","検証済みの映像区間を削除します。再試行時は映像を最初から処理します。元TS・完成動画・ログは保持します。","削除する","キャンセル"))return;client.Send("clear-encode-cache",j.Id);status.Text="ワーカーにキャッシュ削除を依頼しました。";}))));
  }if(results.Children.Count==0)results.Children.Add(Pixel.Text("詳細パイプラインやTS診断の結果があるジョブはまだありません。",14,Pixel.Muted));
 }
}
sealed class SignalTimeline(SignalReport report,double duration):IDrawable {
 public void Draw(ICanvas canvas,RectF rect){canvas.FillColor=Pixel.Tint;canvas.FillRoundedRectangle(rect,8);foreach(var issue in report.Issues){float x=(float)Math.Clamp(issue.Seconds/Math.Max(1,duration),0,1)*rect.Width;canvas.StrokeColor=issue.Kind.Contains("時刻")?Pixel.Blue:Color.FromArgb("#B3261E");canvas.DrawLine(x,12,x,rect.Height-12);}canvas.FontColor=Pixel.Ink;canvas.FontSize=11;canvas.DrawString("0",6,0,100,16,HorizontalAlignment.Left,VerticalAlignment.Top);canvas.DrawString(Commercials.Time(duration),rect.Width-110,0,104,16,HorizontalAlignment.Right,VerticalAlignment.Top);}
}
