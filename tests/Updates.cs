using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;

static partial class Tests {
 static async Task UpdateLive() {
  using var http=new HttpClient();var found=await Updates.Check(http,new Version(0,0,0));
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
  string Release(string tag,bool draft=false,bool prerelease=false)=>"{\"tag_name\":\""+tag+"\",\"draft\":"+draft.ToString().ToLowerInvariant()+",\"prerelease\":"+prerelease.ToString().ToLowerInvariant()+"}";
  handler.Body=Release("v0.2.5");var result=await Updates.Check(http,new Version(0,2,4,0));
  Check(result.Available&&result.Version=="v0.2.5","new GitHub release prompts update");
  Check(handler.Url==Updates.ApiUrl&&handler.Agent.Contains("TS-Encode-Flow/"),"updates query fixed GitHub repository with User-Agent");
  handler.Body=Release("v0.2.4");Check(!(await Updates.Check(http,new Version(0,2,4,0))).Available,"equal three-part tag does not prompt four-part assembly version");
  handler.Body=Release("v0.2.3");Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"older release never prompts downgrade");
  handler.Body=Release("v0.2.10");Check((await Updates.Check(http,new Version(0,2,9))).Available,"release comparison is numeric");
  handler.Body=Release("v1.0.0",draft:true);Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"draft releases ignored");
  handler.Body=Release("v1.0.0",prerelease:true);Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"prereleases ignored");
  handler.Body=Release("bad-tag");Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"invalid version does not prompt update");
  handler.Body="not-json";Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"malformed update response handled");
  handler.Code=HttpStatusCode.NotFound;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("まだありません"),"missing release is not reported as up to date");
  handler.Code=HttpStatusCode.Forbidden;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("利用制限"),"GitHub rate limit reported");
  handler.Code=HttpStatusCode.InternalServerError;Check(!(await Updates.Check(http,new Version(0,2,4))).Available,"server errors handled without interrupting encoding");
  handler.Fail=true;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("接続"),"offline update check handled");
  handler.Fail=false;handler.Cancel=true;Check((await Updates.Check(http,new Version(0,2,4))).Message.Contains("タイムアウト"),"update timeout handled");
 }
}
