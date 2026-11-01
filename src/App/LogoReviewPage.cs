using TsEncodeFlow;
namespace EncodeFlow.App;
sealed class LogoReviewPage:ContentPage {
 public static async Task Open(EngineClient client,Job job){var page=new LogoReviewPage(client,job);var closed=new TaskCompletionSource();page.Unloaded+=(s,e)=>closed.TrySetResult();await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(new NavigationPage(page));await closed.Task;}
 LogoReviewPage(EngineClient client,Job job) {
  Title="生成ロゴの確認";Padding=24;var bitmap=LogoCandidates.Read(job.LogoCandidate,job.Metadata);
  var image=new LogoImageView(bitmap,0);
  var layout=new Grid {RowDefinitions=new RowDefinitionCollection {new(GridLength.Star),new(GridLength.Auto)},RowSpacing=16};layout.Add(image,0,0);
  layout.Add(Pixel.Stack(Pixel.Text("局ロゴだけが抽出されていますか？",24,null,true),Pixel.Text("通常の背景判定で生成できなかったため、背景の一部に文字があっても生成する方式で候補を作りました。番組名・字幕・大きなテロップが見える場合は登録せず、ロゴの見えている部分だけに範囲を絞ってください。灰色の市松模様は透明度確認用です。",15,Pixel.Muted),Pixel.Row(Pixel.Button("局ロゴとして保存して再開",async()=>{await Decide(client,job,true);await Navigation.PopModalAsync();},true),Pixel.Button("登録せず範囲を選び直す",async()=>{await Decide(client,job,false);await Navigation.PopModalAsync();}),Pixel.Button("後で確認する",async()=>await Navigation.PopModalAsync()))),0,1);Content=layout;
 }
 static async Task Decide(EngineClient client,Job job,bool approve) {
  string path=Path.Combine(client.Data,"Inbox",Guid.NewGuid().ToString("N")+".json");Store.Save(path,new QueueManager.Request {Action=approve?"approve-logo":"reject-logo",JobId=job.Id,Fingerprint=job.Fingerprint});client.Wake();
  for(int i=0;i<150;i++){await Task.Delay(100);if(File.Exists(path+".error"))throw new IOException("適用できません。ジョブの状態と入力ファイルを確認してください。");if(!File.Exists(path)){var current=Integration.ReadJobs(client.Data).FirstOrDefault(j=>j.Id==job.Id);if(current!=null&&(approve?current.State!=State.NeedsLogoReview&&StationLogos.Find(client.Data,current)!=null:current.State==State.NeedsLogo))return;}}
  throw new TimeoutException("ワーカーの適用を確認できません。キューを確認してください。");
 }
}
