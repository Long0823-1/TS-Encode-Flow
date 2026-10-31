using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TsEncodeFlow {
 public sealed class ProcessResult {public int ExitCode;public string Output,Error;}
 public static class Processes {
  public static string Quote(string value) {
   if(value.IndexOf('\0')>=0)throw new ArgumentException("NULは使用できません。");
   var b=new StringBuilder("\"");int slash=0;
   foreach(char ch in value){if(ch=='\\'){slash++;continue;}b.Append('\\',ch=='"'?slash*2+1:slash);slash=0;b.Append(ch);}b.Append('\\',slash*2);return b.Append('"').ToString();
  }
  public static List<string> Split(string command) {
   var args=new List<string>();var b=new StringBuilder();bool quoted=false,started=false;
   for(int i=0;i<command.Length;i++) {
    char c=command[i];if(c=='\0'||c=='\r'||c=='\n')throw new ArgumentException("追加引数に改行は使えません。");
    if(c=='\\') {int n=0;while(i<command.Length&&command[i]=='\\'){n++;i++;}if(i<command.Length&&command[i]=='"'){b.Append('\\',n/2);if(n%2==1)b.Append('"');else quoted=!quoted;started=true;}else{b.Append('\\',n);i--;}continue;}
    if(c=='"'){quoted=!quoted;started=true;continue;}if(char.IsWhiteSpace(c)&&!quoted){if(started){args.Add(b.ToString());b.Clear();started=false;}}else{b.Append(c);started=true;}
   }
   if(quoted)throw new ArgumentException("追加引数の引用符が閉じていません。");if(started)args.Add(b.ToString());return args;
  }
  public static async Task<ProcessResult> Run(string exe,IEnumerable<string> args,CancellationToken cancellation,Action<string> log=null,int timeoutSeconds=0) {
   if(!File.Exists(exe))throw new FileNotFoundException("ツールが見つかりません。設定タブでパスを指定してください。",exe);
   using(var p=new Process {StartInfo=new ProcessStartInfo(exe,string.Join(" ",System.Linq.Enumerable.Select(args,Quote))) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(exe)}})
   using(var job=new ChildJob())using(var timeout=new CancellationTokenSource())using(var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,timeout.Token)) {
    if(timeoutSeconds>0)timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
    var output=new StringBuilder();var error=new StringBuilder();object sync=new object();
    p.OutputDataReceived+=(s,e)=>{if(e.Data==null)return;lock(sync){if(output.Length<8*1024*1024)output.AppendLine(e.Data);log?.Invoke(e.Data);}};
    p.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;lock(sync){if(error.Length<2*1024*1024)error.AppendLine(e.Data);log?.Invoke(e.Data);}};
    p.Start();job.Assign(p);p.BeginOutputReadLine();p.BeginErrorReadLine();
    using(linked.Token.Register(()=>{job.Kill();try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){}}))await Task.Run(()=>p.WaitForExit());
    cancellation.ThrowIfCancellationRequested();if(timeout.IsCancellationRequested)throw new TimeoutException(Path.GetFileName(exe)+"がタイムアウトしました。");
    return new ProcessResult {ExitCode=p.ExitCode,Output=output.ToString(),Error=error.ToString()};
   }
  }
 }
 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE also stops encoder muxer children on cancellation/exit.
 sealed class ChildJob:IDisposable {
  IntPtr handle;
  [StructLayout(LayoutKind.Sequential)] struct Basic {public long ProcessTime,JobTime;public uint Flags;public UIntPtr Min,Max;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
  [StructLayout(LayoutKind.Sequential)] struct Io {public ulong A,B,C,D,E,F;}
  [StructLayout(LayoutKind.Sequential)] struct Limits {public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
  [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job,int info,ref Limits limits,uint size);
  [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
  [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr job,uint code);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  public ChildJob(){handle=CreateJobObject(IntPtr.Zero,null);var limits=new Limits();limits.Basic.Flags=0x2000;if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits)))){Dispose();throw new IOException("子プロセス管理を初期化できません。");}}
  public void Assign(Process p){if(!AssignProcessToJobObject(handle,p.Handle)){try{p.Kill();}catch{}throw new IOException("子プロセス管理に登録できません。");}}
  public void Kill(){if(handle!=IntPtr.Zero)TerminateJobObject(handle,1);}
  public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
 }
}
