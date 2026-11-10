using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TsEncodeFlow;
static partial class Tests {
 static async Task UpdateTransactions(string[] args) {
  string fixture=Path.GetFullPath(Option(args,"--update-transactions")),root=Path.GetFullPath(Option(args,"--out"));Directory.CreateDirectory(root);
  for(int mode=0;mode<3;mode++) {
   string work=Path.Combine(root,mode.ToString()),data=Path.Combine(work,"data"),stage=Path.Combine(work,"stage"),dst=Path.Combine(work,"installed");Directory.CreateDirectory(data);Directory.CreateDirectory(stage);Directory.CreateDirectory(dst);
   foreach(string f in Directory.GetFiles(fixture)){File.Copy(f,Path.Combine(stage,Path.GetFileName(f)),true);File.Copy(f,Path.Combine(dst,Path.GetFileName(f)),true);}
   File.WriteAllText(Path.Combine(dst,"preserve.txt"),"user settings");File.WriteAllText(Path.Combine(dst,"a.txt"),"old");File.WriteAllText(Path.Combine(stage,"a.txt"),"new");File.WriteAllText(Path.Combine(dst,"z.txt"),"locked original");File.WriteAllText(Path.Combine(stage,"z.txt"),"new tail");
   var state=new UpdateState {Phase="applying"};SelfUpdate.Write(data,state);
   string manifest=Path.Combine(work,"manifest.json");var entries=Directory.GetFiles(stage).OrderBy(Path.GetFileName,StringComparer.Ordinal).Select(f=>new {Relative=Path.GetFileName(f),Hash=mode==1?new string('0',64):Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}).ToArray();File.WriteAllText(manifest,JsonSerializer.Serialize(entries));
   string Q(string p)=>"'"+p.Replace("'","''")+"'";string script=Path.Combine(work,"apply.ps1");File.WriteAllText(script,"$src="+Q(stage)+"\n$dst="+Q(dst)+"\n$data="+Q(data)+"\n$manifest="+Q(manifest)+"\n$self=-1\n"+SelfUpdate.ApplyScript,new UTF8Encoding(true));
   using(var locked=mode==2?File.Open(Path.Combine(dst,"z.txt"),FileMode.Open,FileAccess.ReadWrite,FileShare.None):null){using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File "+Processes.Quote(script)){UseShellExecute=false,CreateNoWindow=true});await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}
   for(int i=0;i<30&&!File.Exists(Path.Combine(data,"fixture-restarted.txt"));i++)await Task.Delay(100);
   var result=SelfUpdate.Read(data);Check(File.ReadAllText(Path.Combine(dst,"preserve.txt"))=="user settings","updater preserves unrelated user files mode "+mode);
   if(mode==0)Check(result.Phase=="installed"&&File.ReadAllText(Path.Combine(dst,"a.txt"))=="new"&&File.Exists(Path.Combine(data,"fixture-restarted.txt")),"updater copies packaged files and restarts app inside the app flow");
   else Check(result.Phase=="error"&&File.ReadAllText(Path.Combine(dst,"a.txt"))=="old"&&File.ReadAllText(Path.Combine(dst,"z.txt"))=="locked original","updater rejects changed stage or rolls back after copy failure mode "+mode);
  }
 }
}
