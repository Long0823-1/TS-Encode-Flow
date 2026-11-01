using System.Globalization;
using Microsoft.Maui.Graphics;
using TsEncodeFlow;

namespace EncodeFlow.App;
sealed class LogoAreaPage:ContentPage {
 readonly EngineClient client;readonly Job job;readonly string fingerprint;
 readonly CancellationTokenSource stop=new();
 readonly GraphicsView preview=new(){MinimumHeightRequest=160,BackgroundColor=Colors.Black};
 readonly AreaDrawing drawing=new();readonly Entry seconds=Pixel.Entry("30");
 readonly Label selected=Pixel.Text("プレビュー上でロゴ全体をドラッグして囲んでください。",14,Pixel.Muted);
 readonly Entry x=Pixel.Entry(""),y=Pixel.Entry(""),w=Pixel.Entry(""),h=Pixel.Entry("");
 readonly Button save;LogoArea? area;PointF start;bool dragging,loading;string? previewFile;
 public static async Task Open(EngineClient client,Job job) {var page=new LogoAreaPage(client,job);var closed=new TaskCompletionSource();page.Unloaded+=(s,e)=>closed.TrySetResult();await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));await closed.Task;}
 LogoAreaPage(EngineClient client,Job original) {
  this.client=client;job=Store.Clone(original);seconds.Text=Math.Min(30,Math.Max(0,job.Metadata.Duration/2)).ToString("0.###",CultureInfo.InvariantCulture);fingerprint=Store.Fingerprint(job.Input);if(fingerprint!=job.Fingerprint)throw new IOException("解析後に入力TSが変更されています。中止後、再試行してください。");Title="放送局ロゴの範囲指定";Padding=24;
  preview.Drawable=drawing;preview.StartInteraction+=(s,e)=>{if(drawing.Image==null||loading||e.Touches.Length==0)return;start=e.Touches[0];dragging=drawing.Bounds(preview.Width,preview.Height).Contains(start);};
  preview.DragInteraction+=(s,e)=>{if(dragging&&e.Touches.Length>0)Select(e.Touches[0]);};
  preview.EndInteraction+=(s,e)=>{if(dragging&&e.Touches.Length>0)Select(e.Touches[0]);dragging=false;};
  preview.CancelInteraction+=(s,e)=>dragging=false;
  foreach(var entry in new[]{x,y,w,h})entry.WidthRequest=90;
  save=Pixel.Button("この局の範囲を保存して再開",Save,true);save.IsEnabled=false;
  var controls=Pixel.Stack(Pixel.Text((job.Metadata.Channel.Length>0?job.Metadata.Channel:"局名不明")+" · "+job.Metadata.Width+" × "+job.Metadata.Height,22,null,true),Pixel.Text("ロゴが見える本編を表示し、番組テロップを避け、局ロゴの見える部分と少しの余白だけを囲んでください。元映像の座標で保存し、同じ局・解像度で再利用します。CM中などロゴがない場面は時間を変えてください。",14,Pixel.Muted),Pixel.Row(Pixel.Text("表示位置（秒）"),seconds,Pixel.Button("プレビューを表示",Load)),selected,Pixel.Row(Pixel.Field("X",x),Pixel.Field("Y",y),Pixel.Field("幅",w),Pixel.Field("高さ",h)),Pixel.Row(save,Pixel.Button("閉じる",async()=>await Navigation.PopModalAsync())));
  seconds.WidthRequest=100;
  var layout=new Grid {RowDefinitions=new RowDefinitionCollection {new(GridLength.Star),new(GridLength.Auto)},RowSpacing=16};layout.Add(preview,0,0);layout.Add(Pixel.Scroll(controls),0,1);Content=layout;
  Loaded+=async(s,e)=>{try{await Load();}catch(Exception ex){Pixel.LogError("ロゴプレビュー",ex);selected.Text=ex.Message;}};
  Unloaded+=(s,e)=>{stop.Cancel();drawing.Image?.Dispose();drawing.Image=null;if(previewFile!=null)try{File.Delete(previewFile);}catch(IOException){}};
 }
 void Select(PointF point) {
  var b=drawing.Bounds(preview.Width,preview.Height);if(b.Width<=0||b.Height<=0)return;
  area=LogoArea.FromSelection((start.X-b.X)/b.Width,(start.Y-b.Y)/b.Height,(point.X-b.X)/b.Width,(point.Y-b.Y)/b.Height,job.Metadata.Width,job.Metadata.Height);
  x.Text=area.X.ToString();y.Text=area.Y.ToString();w.Text=area.Width.ToString();h.Text=area.Height.ToString();drawing.Area=area;drawing.SourceWidth=job.Metadata.Width;drawing.SourceHeight=job.Metadata.Height;preview.Invalidate();selected.Text="選択範囲（元映像のピクセル）："+area;
 }
 async Task Load() {
  if(loading)return;loading=true;save.IsEnabled=false;
  try {
   if(!double.TryParse(seconds.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double t)||!double.IsFinite(t)||t<0||t>=job.Metadata.Duration)throw new ArgumentException("表示位置は動画内の秒数を指定してください。");
   if(Store.Fingerprint(job.Input)!=fingerprint)throw new IOException("入力TSが変更されています。キューから開き直してください。");
   selected.Text="1フレームを読み込み中…";Directory.CreateDirectory(Path.Combine(client.Data,"LogoPreview"));string file=Path.Combine(client.Data,"LogoPreview",Guid.NewGuid().ToString("N")+".png");
   try {
    var result=await Processes.Run(client.Settings.Ffmpeg,new[]{"-v","error","-ss",t.ToString("R",CultureInfo.InvariantCulture),"-i",job.Input,"-map","0:"+job.Metadata.VideoIndex,"-frames:v","1","-vf","scale=trunc(iw*sar/2)*2:ih,setsar=1,scale=960:-2","-an","-sn","-dn","-y",file},stop.Token,null,60);
    stop.Token.ThrowIfCancellationRequested();if(result.ExitCode!=0||!File.Exists(file))throw new IOException("プレビューを取得できません。表示位置とFFmpegの設定を確認してください。\n"+result.Error);
    using var stream=File.OpenRead(file);var img=Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(stream);drawing.Image?.Dispose();drawing.Image=img;preview.Invalidate();
    if(previewFile!=null)File.Delete(previewFile);previewFile=file;file=null!;selected.Text="ロゴ全体をドラッグして囲んでください。座標の数値入力もできます。";
   }finally{if(file!=null&&File.Exists(file))File.Delete(file);}
  }finally{loading=false;save.IsEnabled=drawing.Image!=null&&!stop.IsCancellationRequested;}
 }
 async Task Save() {
  if(!int.TryParse(x.Text,out int left)||!int.TryParse(y.Text,out int top)||!int.TryParse(w.Text,out int width)||!int.TryParse(h.Text,out int height))throw new ArgumentException("ロゴを囲むか、X・Y・幅・高さを整数で入力してください。");
  var value=new LogoArea {X=left,Y=top,Width=width,Height=height};value.Validate(job.Metadata);
  if(Store.Fingerprint(job.Input)!=fingerprint)throw new IOException("入力TSが変更されています。キューから開き直してください。");
  string request=Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json");
  Store.Save(request,new QueueManager.Request {Action="set-logo-area",JobId=job.Id,Fingerprint=fingerprint,LogoArea=value});client.Wake();
  for(int i=0;i<150;i++){await Task.Delay(100,stop.Token);if(File.Exists(request+".error"))throw new IOException("範囲を適用できません。ジョブの状態を確認してください。");if(!File.Exists(request)){var saved=StationLogos.Read(client.Data,job).Area;if(saved!=null&&saved.ToString()==value.ToString()){await Navigation.PopModalAsync();return;}}}
  throw new TimeoutException("ワーカーの適用を確認できません。キューを確認してください。");
 }
 sealed class AreaDrawing:IDrawable {
  public Microsoft.Maui.Graphics.IImage? Image;public LogoArea? Area;public int SourceWidth,SourceHeight;
  public RectF Bounds(double width,double height) {if(Image==null||width<=0||height<=0)return new();float scale=(float)Math.Min(width/Image.Width,height/Image.Height);float w=Image.Width*scale,h=Image.Height*scale;return new((float)(width-w)/2,(float)(height-h)/2,w,h);}
  public void Draw(ICanvas canvas,RectF dirty) {
   if(Image==null)return;var b=Bounds(dirty.Width,dirty.Height);canvas.DrawImage(Image,b.X,b.Y,b.Width,b.Height);
   if(Area!=null&&SourceWidth>0&&SourceHeight>0){canvas.StrokeColor=Colors.Yellow;canvas.StrokeSize=3;canvas.DrawRectangle(b.X+(float)Area.X/SourceWidth*b.Width,b.Y+(float)Area.Y/SourceHeight*b.Height,(float)Area.Width/SourceWidth*b.Width,(float)Area.Height/SourceHeight*b.Height);}
  }
 }
}

sealed class StationLogosView:ContentView {
 public StationLogosView(EngineClient client)=>Content=Build(client);
 View Build(EngineClient client) {
  var rows=Pixel.Stack(Pixel.Text("未登録の局だけ、一度ロゴを囲む",24,null,true),Pixel.Text("ロゴは放送局IDと元映像の解像度ごとに保存します。既存のLGDも再利用します。BATで追加した録画も、ここで指定してから自動処理を再開できます。",14,Pixel.Muted),Pixel.Button("一覧を更新",()=>Content=Build(client)),Pixel.Button("保存フォルダーを開く",()=>{var p=Path.Combine(client.Data,"Logos");Directory.CreateDirectory(p);Pickers.Open(p);}));
  client.Refresh();var groups=client.Jobs.Where(j=>j.Metadata!=null).GroupBy(j=>StationLogos.Key(j)).OrderBy(g=>g.Key).ToList();
  foreach(var group in groups) {
   var review=group.FirstOrDefault(j=>j.State==State.NeedsLogoReview);var pending=group.FirstOrDefault(j=>StationLogos.CanConfigure(client.Data,j,client.Settings));var job=review??pending??group.First();string? logo=StationLogos.Find(client.Data,job);var area=StationLogos.Read(client.Data,job).Area;
   var card=Pixel.Stack(Pixel.Text((string.IsNullOrWhiteSpace(job.Metadata.Channel)?"局名不明":job.Metadata.Channel)+" · "+job.Metadata.Width+" × "+job.Metadata.Height,18,null,true),Pixel.Text(group.Key,12,Pixel.Muted),Pixel.Text(review!=null?"生成画像の確認待ち（まだ局ロゴには登録していません）":logo!=null?"登録済み："+Path.GetFileName(logo):area!=null?"範囲登録済み："+area:"ロゴ未登録・範囲の指定が必要",14,Pixel.Muted));
   if(review!=null)card.Children.Add(Pixel.Button("生成ロゴを確認",async()=>{await LogoReviewPage.Open(client,review);Content=Build(client);},true));
   if(pending!=null)card.Children.Add(Pixel.Button("ロゴ範囲を指定",async()=>{await LogoAreaPage.Open(client,pending);Content=Build(client);},true));rows.Children.Add(Pixel.Card(card));
  }
  var saved=StationLogos.List(client.Data).Where(s=>!groups.Any(g=>g.Key==s.Key)).ToList();
  foreach(var station in saved)rows.Children.Add(Pixel.Card(Pixel.Stack(Pixel.Text(station.Channel??station.Key,18,null,true),Pixel.Text(station.Key,12,Pixel.Muted),Pixel.Text(station.Area!=null?"保存範囲："+station.Area:"局別LGD保存済み",14,Pixel.Muted))));
  if(groups.Count==0&&saved.Count==0)rows.Children.Add(Pixel.Text("CM解析するTSをキューに追加すると、放送局が表示されます。",14,Pixel.Muted));return Pixel.Scroll(rows);
 }
}
