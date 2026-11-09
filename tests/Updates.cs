using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task UpdateLive() {
  using var http=new HttpClient();var found=await Updates.Check(http,new Version(0,0,0));
  Console.WriteLine("GitHub live check: "+found.Message);
  Check(found.Available,"public GitHub API discovers published release without credentials");
  var same=await Updates.Check(http,Version.Parse(found.Version.TrimStart('v','V')));
  Check(!same.Available&&same.Message.Contains("最新版"),"public GitHub API confirms equal installed release");
 }
 sealed class ReleaseHandler:HttpMessageHandler {
  public HttpStatusCode Code=HttpStatusCode.OK;
  public string Body="",Agent="",Url="";
  public bool Fail,Cancel;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
   Agent=request.Headers.UserAgent.ToString();Url=request.RequestUri.ToString();
   if(Fail)throw new HttpRequestException("offline");if(Cancel)throw new OperationCanceledException();
   return Task.FromResult(new HttpResponseMessage(Code){Content=new StringContent(Body)});
  }
 }
 static async Task UpdateTests() {
  using var handler=new ReleaseHandler();using var http=new HttpClient(handler);
  string Asset(string version,string digest="")=>"{\"name\":\"TS-Encode-Flow-"+version+"-win-x64.zip"+"\",\"browser_download_url\":\"https://github.com/Long0823-1/TS-Encode-Flow/releases/download/v"+version+"/TS-Encode-Flow-"+version+"-win-x64.zip"+"\",\"digest\":\""+digest+"\"}";
  string Release(string tag,bool draft=false,bool prerelease=false,string published="2026-01-01T00:00:00Z",string assets=null)=>"{\"tag_name\":\""+tag+"\",\"draft\":"+draft.ToString().ToLowerInvariant()+",\"prerelease\":"+prerelease.ToString().ToLowerInvariant()+",\"published_at\":\""+published+"\",\"assets\":["+(assets??Asset(tag.TrimStart('v')))+"]}";
  string List(params string[] items)=>"["+string.Join(",",items)+"]";
  handler.Body=List(Release("v0.2.5"));var result=await Updates.Check(http,new Version(0,2,4,0));
  Check(result.Available&&result.Version=="v0.2.5","new GitHub release prompts update");
  Check(handler.Url==Updates.ListUrl&&handler.Agent.Contains("TS-Encode-Flow/"),"updates query the release list with User-Agent");
  handler.Body=List(Release("v0.2.4"));Check(!(await Updates.Check(http,new Version(0,2,4,0))).Available,"equal three-part tag does not prompt four-part assembly version");
  handler.Body=List(Release("v0.2.3"));Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"older release never prompts downgrade");
  handler.Body=List(Release("v0.2.10"));Check((await Updates.Check(http,new Version(0,2,9))).Available,"release comparison is numeric");
  handler.Body=List(Release("v1.0.0",draft:true));Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"draft releases ignored");
  handler.Body=List(Release("v0.3.0",prerelease:true));Check((await Updates.Check(http,new Version(0,2,4))).Available,"prereleases are update targets (user wants the newest)");
  handler.Body=List(Release("bad-tag"));Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"invalid version does not prompt update");
  handler.Body=List(Release("v2026.11.09.2"));Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"date-style CI tags are not update targets");
  handler.Body=List(Release("v0.2.6",assets:""));Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"release without the app zip is not an update target");
  handler.Body=List(Release("v0.2.5",published:"2026-01-02T00:00:00Z"),Release("v0.2.6",published:"2026-01-01T00:00:00Z"));
  Check((await Updates.Check(http,new Version(0,2,4))).Version=="v0.2.6","highest version wins over newest publish date");
  handler.Body="not-json";Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"malformed update response handled");
  handler.Code=HttpStatusCode.NotFound;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("まだありません"),"missing release is not reported as up to date");
  handler.Code=HttpStatusCode.Forbidden;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("利用制限"),"GitHub rate limit reported");
  handler.Code=HttpStatusCode.InternalServerError;Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"server errors handled without interrupting encoding");
  handler.Fail=true;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("接続"),"offline update check handled");
  handler.Fail=false;handler.Cancel=true;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("タイムアウト"),"update timeout handled");
  // PickLatest details: digest passthrough and asset selection.
  using var doc=JsonDocument.Parse(List(Release("v0.2.7",assets:Asset("0.2.7","sha256:abc123"))));
  var picked=Updates.PickLatest(doc.RootElement);
  Check(picked!=null&&picked.Tag=="v0.2.7"&&picked.Digest=="sha256:abc123"&&picked.ZipUrl.EndsWith("-win-x64.zip"),"pick latest returns the zip asset and its digest");
  Check(Updates.TryVersion("v0.6.8",out var parsed)&&parsed==new Version(0,6,8),"version tags parse");
  Check(Updates.IsAppZip("TS-Encode-Flow-0.6.9-win-x64.zip")&&Updates.IsAppZip("TS-Encode-Flow-v0.6.7-windows-x64.zip")==false&&!Updates.IsAppZip("TS-Encode-Flow-0.6.9-source.zip"),"app zip asset pattern matches workflow naming only");
  Check(!Updates.TryVersion("v0.6.8-beta",out _),"suffixed tags rejected");
 }
}
