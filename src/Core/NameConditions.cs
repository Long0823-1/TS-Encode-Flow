using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TsEncodeFlow {
 public static class NameConditions {
  static readonly string[] Fields={"Title","Channel","OriginalName","Start","ServiceId","EventId","NetworkId","TransportId","Episode","Width","Height","Codec","Engine"};
  static readonly string[] Numbers={"ServiceId","EventId","NetworkId","TransportId","Episode","Width","Height"};
  sealed class Frame {public bool Parent,Condition,HasElse;}
  static bool Condition(string text,IDictionary<string,string> values) {
   var m=Regex.Match(text,@"^\s*(!?)\s*([A-Za-z][A-Za-z0-9]*)(?:\s*(==|!=|>=|<=|>|<)\s*(.+?))?\s*$");
   if(!m.Success)throw new ArgumentException("if条件は {if:Episode}、{if:Channel==局名}、{if:Width>=1920} の形式です。");
   string field=m.Groups[2].Value;if(!values.TryGetValue(field,out string actual))throw new ArgumentException("if条件の未知の項目："+field);
   actual??="";string op=m.Groups[3].Value,literal=m.Groups[4].Value.Trim();bool result;
   if(op.Length==0)result=actual.Length>0;
   else {
    if(literal.StartsWith("\"")||literal.StartsWith("'")){if(literal.Length<2||literal[^1]!=literal[0])throw new ArgumentException("if条件の引用符が閉じていません。");literal=literal.Substring(1,literal.Length-2);}
    if(op=="=="||op=="!="){result=string.Equals(actual,literal,StringComparison.OrdinalIgnoreCase);if(op=="!=")result=!result;}
    else {
     if(!Numbers.Contains(field,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("大小比較は話数・解像度・IDの数値項目で使用してください。");
     if(!double.TryParse(literal,NumberStyles.Float,CultureInfo.InvariantCulture,out double right)||!double.IsFinite(right))throw new ArgumentException("ifの比較値を数値で入力してください。");
     if(string.IsNullOrEmpty(actual))result=false;
     else {if(!double.TryParse(actual,NumberStyles.Float,CultureInfo.InvariantCulture,out double left)||!double.IsFinite(left))throw new ArgumentException("ifで数値として比較できません："+field);result=op==">="?left>=right:op=="<="?left<=right:op==">"?left>right:left<right;}
    }
   }
   return m.Groups[1].Length>0?!result:result;
  }
  public static void Validate(string template) {
   var values=Fields.ToDictionary(x=>x,x=>"",StringComparer.OrdinalIgnoreCase);Expand(template,values,true);
  }
  public static string Expand(string template,IDictionary<string,string> values,bool validateOnly=false) {
   if(template==null||template.Length>4096)throw new ArgumentException("ファイル名マクロは4096文字以内です。");
   var output=new StringBuilder();var stack=new Stack<Frame>();bool active=true;int cursor=0;
   foreach(Match token in Regex.Matches(template,@"\{([^{}]*)\}")) {
    if(active)output.Append(template,cursor,token.Index-cursor);string tag=token.Groups[1].Value;
    if(tag.StartsWith("if:",StringComparison.OrdinalIgnoreCase)){if(stack.Count>=8)throw new ArgumentException("ifの入れ子は8段までです。");bool condition=Condition(tag.Substring(3),values);stack.Push(new(){Parent=active,Condition=condition});active=active&&condition;}
    else if(tag.Equals("else",StringComparison.OrdinalIgnoreCase)){if(stack.Count==0||stack.Peek().HasElse)throw new ArgumentException("elseの位置、または重複を確認してください。");var frame=stack.Peek();frame.HasElse=true;active=frame.Parent&&!frame.Condition;}
    else if(tag.Equals("endif",StringComparison.OrdinalIgnoreCase)){if(stack.Count==0)throw new ArgumentException("対応するifがないendifです。");active=stack.Pop().Parent;}
    else {if(validateOnly&&!Fields.Contains(tag.Split(':')[0],StringComparer.OrdinalIgnoreCase))throw new ArgumentException("未知のマクロ："+token.Value);if(active)output.Append(token.Value);}
    cursor=token.Index+token.Length;
   }
   if(stack.Count>0)throw new ArgumentException("ifに対応する{endif}がありません。");if(active)output.Append(template,cursor,template.Length-cursor);return output.ToString();
  }
 }
}
