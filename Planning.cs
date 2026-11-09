using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public sealed class ChecklistItem
{
 public string Text { get; set; }
 public bool Done { get; set; }
}

public sealed partial class TaskItem
{
 public string DeadlineDate { get; set; }
 public string EndDate { get; set; }
 public bool Unscheduled { get; set; }
 public string Reviewer { get; set; }
 public string SubmittedAt { get; set; }
 public string ExecutedAt { get; set; }
 public string CompletedAt { get; set; }
 public string FollowUpDate { get; set; }
 public string FollowUpNotified { get; set; }
 public string ReminderSnoozeUntil { get; set; }
 public string ReviewNotes { get; set; }
 public string DeletedAt { get; set; }
 public string Project { get; set; }
 public string Tags { get; set; }
 public int Priority { get; set; }
 public int EstimateMinutes { get; set; }
 public double SortOrder { get; set; }
 public List<ChecklistItem> Checklist { get; set; }
 public List<string> Links { get; set; }
 public int RepeatInterval { get; set; }
 public bool RepeatFromCompletion { get; set; }
 public string RepeatWeekdays { get; set; }
 public string RepeatUntil { get; set; }
 public int RepeatLimit { get; set; }
 public int OccurrenceIndex { get; set; }
 public string SeriesId { get; set; }
 public string RecurrenceStart { get; set; }
 public string RepeatExceptions { get; set; }
 [ScriptIgnore] public bool IsDeleted { get { return !String.IsNullOrEmpty(DeletedAt); } }
 [ScriptIgnore] public string SectionHeading { get {
  if(IsDeleted)return "最近删除 · 可恢复";
  if(IsAwaitingReview)return PlanningRules.IsDateBefore(FollowUpDate,DateTime.Today.AddDays(1))?"需要跟进":"等待确认";
  if(IsDone)return "已完成";
  if(Unscheduled)return "收集箱 · 尚未安排";
  DateTime d;if(!PlanningRules.TryDate(DueDate,out d))return "未安排";
  return d<DateTime.Today?"之前安排 · "+d.ToString("M月d日"):d==DateTime.Today?"今天":d==DateTime.Today.AddDays(1)?"明天":d.ToString("M月d日 dddd",CultureInfo.GetCultureInfo("zh-CN"));
 } }
 [ScriptIgnore] public string PlanningMeta { get {
  var parts=new List<string>();
  if(IsDeleted){DateTime deleted;if(DateTime.TryParse(DeletedAt,out deleted))parts.Add("删除于 "+deleted.ToString("M月d日 HH:mm"));return String.Join(" · ",parts);}
  if(Priority>0)parts.Add(Priority>=3?"高优先级":Priority==2?"中优先级":"低优先级");
  if(!String.IsNullOrWhiteSpace(Project))parts.Add(Project);
  if(IsAwaitingReview){parts.Add(String.IsNullOrWhiteSpace(Reviewer)?"等待确认":"等待 "+Reviewer);DateTime submitted;if(DateTime.TryParse(SubmittedAt,out submitted))parts.Add("已等待 "+Math.Max(0,(DateTime.Today-submitted.Date).Days)+" 天");if(!String.IsNullOrEmpty(FollowUpDate))parts.Add("跟进 "+FollowUpDate);}
  if(!String.IsNullOrEmpty(DeadlineDate)){DateTime deadline;if(PlanningRules.TryDate(DeadlineDate,out deadline))parts.Add((!IsDone&&deadline<DateTime.Today?"已逾期 · ":"截止 ")+deadline.ToString("M月d日"));}
  if(!String.IsNullOrEmpty(EndDate)&&EndDate!=DueDate)parts.Add("持续至 "+EndDate);
  if(Checklist!=null&&Checklist.Count>0)parts.Add("子项 "+Checklist.Count(x=>x.Done)+"/"+Checklist.Count);
  if(EstimateMinutes>0)parts.Add("预计 "+EstimateMinutes+" 分钟");
  if(!String.IsNullOrWhiteSpace(Tags))parts.Add("标签 "+Tags);
  return String.Join(" · ",parts);
 } }
 public void RefreshPlanning(){Notify("PlanningMeta");Notify("SectionHeading");Notify("IsDeleted");Notify("Meta");}
}

internal static class PlanningRules
{
 public static bool TryDate(string s,out DateTime value){return DateTime.TryParseExact(s,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value);}
 public static bool IsDateBefore(string s,DateTime limit){DateTime d;return TryDate(s,out d)&&d<limit;}
 public static bool Occurs(TaskItem t,DateTime day){if(t.IsDeleted||t.Unscheduled)return false;DateTime start,end;if(!TryDate(t.DueDate,out start))return false;if(!TryDate(t.EndDate,out end)||end<start)end=start;return day.Date>=start&&day.Date<=end;}
 public static void Normalize(TaskItem t){
  t.Priority=Math.Max(0,Math.Min(3,t.Priority));t.EstimateMinutes=Math.Max(0,Math.Min(1440,t.EstimateMinutes));
  t.RepeatInterval=Math.Max(1,Math.Min(365,t.RepeatInterval));t.RepeatLimit=Math.Max(0,t.RepeatLimit);
  if(t.Checklist==null)t.Checklist=new List<ChecklistItem>();else t.Checklist=t.Checklist.Where(x=>x!=null&&!String.IsNullOrWhiteSpace(x.Text)).ToList();
  if(t.Links==null)t.Links=new List<string>();else t.Links=t.Links.Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct().ToList();
  if(String.IsNullOrEmpty(t.SeriesId))t.SeriesId=t.Id;
  if(String.IsNullOrEmpty(t.RecurrenceStart))t.RecurrenceStart=t.DueDate;
 }
 public static string Validate(TaskItem t){DateTime plan,end,dummy;if(String.IsNullOrWhiteSpace(t.Title))return "存在空标题";if(!TryDate(t.DueDate,out plan))return "计划日期格式无效";if(!String.IsNullOrEmpty(t.DeadlineDate)&&!TryDate(t.DeadlineDate,out dummy))return "截止日期格式无效";if(!String.IsNullOrEmpty(t.EndDate)&&(!TryDate(t.EndDate,out end)||end<plan))return "跨日结束早于计划日期或格式无效";if(!String.IsNullOrEmpty(t.FollowUpDate)&&!TryDate(t.FollowUpDate,out dummy))return "跟进日期格式无效";return null;}
 public static DateTime? NextDate(TaskItem t,DateTime completed){
  if(t.Repeat=="不重复"||t.RepeatLimit>0&&t.OccurrenceIndex+1>=t.RepeatLimit)return null;
  DateTime original;if(!TryDate(t.DueDate,out original))return null;
  DateTime anchor;if(!TryDate(t.RecurrenceStart,out anchor))anchor=original;
  DateTime baseDate=t.RepeatFromCompletion?completed.Date:original;
  int interval=Math.Max(1,t.RepeatInterval);
  var exceptions=new HashSet<string>((t.RepeatExceptions??"").Split(new[]{',','，',';','；',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries));
  DateTime candidate=baseDate;
  for(int attempt=0;attempt<4000;attempt++){
   if(t.Repeat=="每天")candidate=candidate.AddDays(interval);
   else if(t.Repeat=="工作日"){int left=interval;while(left>0){candidate=candidate.AddDays(1);if(candidate.DayOfWeek!=DayOfWeek.Saturday&&candidate.DayOfWeek!=DayOfWeek.Sunday)left--;}}
   else if(t.Repeat=="自定义周"){
    var days=new HashSet<int>((t.RepeatWeekdays??"").Split(',').Select(x=>{int n;return Int32.TryParse(x,out n)?n:0;}).Where(x=>x>=1&&x<=7));
    if(days.Count==0)days.Add(((int)original.DayOfWeek+6)%7+1);
    DateTime reference=t.RepeatFromCompletion?baseDate:anchor;DateTime monday=reference.AddDays(-(((int)reference.DayOfWeek+6)%7));
    do{candidate=candidate.AddDays(1);}while(!days.Contains(((int)candidate.DayOfWeek+6)%7+1)||((int)((candidate-monday).TotalDays/7)%interval+interval)%interval!=0);
   }
   else if(t.Repeat=="每周")candidate=candidate.AddDays(interval*7);
   else if(t.Repeat=="每月"||t.Repeat=="每月最后一天"){
    DateTime month=new DateTime(candidate.Year,candidate.Month,1).AddMonths(interval);
    int wanted=t.Repeat=="每月最后一天"?DateTime.DaysInMonth(month.Year,month.Month):t.RepeatFromCompletion?baseDate.Day:anchor.Day;
    candidate=new DateTime(month.Year,month.Month,Math.Min(wanted,DateTime.DaysInMonth(month.Year,month.Month)));
   }
   else if(t.Repeat=="每年"){int year=candidate.Year+interval;candidate=new DateTime(year,anchor.Month,Math.Min(anchor.Day,DateTime.DaysInMonth(year,anchor.Month)));}
   else return null;
   DateTime until;if(TryDate(t.RepeatUntil,out until)&&candidate>until)return null;
   if(!exceptions.Contains(candidate.ToString("yyyy-MM-dd")))return candidate;
  }
  return null;
 }
 public static TaskItem CreateNext(TaskItem t,DateTime completed){
  DateTime? next=NextDate(t,completed);if(!next.HasValue)return null;
  var json=new JavaScriptSerializer();TaskItem copy=json.Deserialize<TaskItem>(json.Serialize(t));
  DateTime old;TryDate(t.DueDate,out old);TimeSpan delta=next.Value-old;
  copy.Id=Guid.NewGuid().ToString();copy.SetState(false,false);copy.DueDate=next.Value.ToString("yyyy-MM-dd");
  DateTime date;if(TryDate(t.DeadlineDate,out date))copy.DeadlineDate=date.Add(delta).ToString("yyyy-MM-dd");if(TryDate(t.EndDate,out date))copy.EndDate=date.Add(delta).ToString("yyyy-MM-dd");
  copy.OccurrenceIndex=t.OccurrenceIndex+1;copy.SeriesId=t.SeriesId??t.Id;copy.RecurrenceCreated=false;copy.Notified="";
  copy.SubmittedAt=copy.ExecutedAt=copy.CompletedAt=copy.FollowUpDate=copy.FollowUpNotified=copy.DeletedAt="";
  copy.ReviewNotes="";if(copy.Checklist!=null)foreach(var item in copy.Checklist)item.Done=false;
  Normalize(copy);return copy;
 }
}

internal sealed class HistoryEntry { public string Json; public string Label; }
internal sealed class PlanningHistory
{
 readonly Stack<HistoryEntry> undo=new Stack<HistoryEntry>(),redo=new Stack<HistoryEntry>();
 public string Current { get; private set; }
 public bool CanUndo { get { return undo.Count>0; } } public bool CanRedo { get { return redo.Count>0; } }
 public string UndoLabel { get { return CanUndo?undo.Peek().Label:""; } }
 public void Reset(string value){Current=value;undo.Clear();redo.Clear();}
 public void Record(string value,string label){if(Current==value)return;undo.Push(new HistoryEntry{Json=Current,Label=label});if(undo.Count>60){var keep=undo.Take(60).Reverse().ToList();undo.Clear();foreach(var item in keep)undo.Push(item);}Current=value;redo.Clear();}
 public string Undo(){if(!CanUndo)return null;var item=undo.Pop();redo.Push(new HistoryEntry{Json=Current,Label=item.Label});Current=item.Json;return Current;}
 public string Redo(){if(!CanRedo)return null;var item=redo.Pop();undo.Push(new HistoryEntry{Json=Current,Label=item.Label});Current=item.Json;return Current;}
}

internal static class BackupVault
{
 public static string DirectoryFor(string path){return Path.Combine(Path.GetDirectoryName(path),"backups");}
 public static IEnumerable<string> List(string path){string dir=DirectoryFor(path);return Directory.Exists(dir)?Directory.GetFiles(dir,"tasks-*.json").OrderByDescending(x=>Path.GetFileName(x)):Enumerable.Empty<string>();}
 public static string Capture(string path,bool force=false){
  if(!File.Exists(path))return null;
  string dir=DirectoryFor(path);var previous=List(path).ToList();byte[] bytes=File.ReadAllBytes(path);
  if(!force&&previous.Count>0){if(File.GetLastWriteTimeUtc(previous[0])>DateTime.UtcNow.AddMinutes(-10))return null;if(File.ReadAllBytes(previous[0]).SequenceEqual(bytes))return null;}
  Directory.CreateDirectory(dir);string destination=Path.Combine(dir,"tasks-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json");
  string temp=destination+".tmp";
  using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
  if(!File.ReadAllBytes(temp).SequenceEqual(bytes))throw new IOException("备份校验失败");File.Move(temp,destination);
  // Retain recent hourly, daily and weekly points instead of only the latest writes.
  var files=List(path).ToList();var keep=new HashSet<string>();var hours=new HashSet<string>();var days=new HashSet<string>();
  foreach(string file in files){DateTime stamp=File.GetLastWriteTimeUtc(file);if(keep.Count<6&&hours.Add(stamp.ToString("yyyyMMddHH")))keep.Add(file);if(days.Count<21&&days.Add(stamp.ToString("yyyyMMdd")))keep.Add(file);}
  foreach(string file in files.Skip(1).Where(x=>!keep.Contains(x)&&File.GetLastWriteTimeUtc(x)<DateTime.UtcNow.AddDays(-1)))File.Delete(file);
  return destination;
 }
}

internal sealed class QuickDraft
{
 public string Title,Date,Time,Category,Repeat,Weekdays;
 public bool HasDate;
 public static QuickDraft Parse(string input,DateTime today){
  var d=new QuickDraft{Title=(input??"").Trim(),Date=today.ToString("yyyy-MM-dd"),Time="",Category="个人",Repeat="不重复"};
  string text=Regex.Replace(d.Title,@"([零一二三四五六七八九十两]{1,3})点",match=>ChineseNumber(match.Groups[1].Value)+"点");DateTime date=today;Match m=Regex.Match(text,@"(?<!\d)(\d{4}-\d{1,2}-\d{1,2})(?!\d)");
  if(m.Success&&DateTime.TryParse(m.Value,out date)){d.HasDate=true;text=text.Remove(m.Index,m.Length);}
  else{m=Regex.Match(text,@"大后天|后天|明天|今天|下周[一二三四五六日天]|本周[一二三四五六日天]|(?<!每)周[一二三四五六日天]");if(m.Success){string token=m.Value;date=today;if(token=="明天")date=today.AddDays(1);else if(token=="后天")date=today.AddDays(2);else if(token=="大后天")date=today.AddDays(3);else if(token.Contains("周")){int index="一二三四五六日".IndexOf(token[token.Length-1]);if(index<0)index=6;date=today.AddDays(-(((int)today.DayOfWeek+6)%7)+index+(token.StartsWith("下周")?7:0));if(!token.StartsWith("本周")&&!token.StartsWith("下周")&&date<today)date=date.AddDays(7);}d.HasDate=true;text=text.Remove(m.Index,m.Length);}}
  m=Regex.Match(text,@"(?:(上午|下午|晚上|中午)\s*)?(\d{1,2})\s*[:：]\s*(\d{2})");
  if(!m.Success)m=Regex.Match(text,@"(上午|下午|晚上|中午)?\s*(\d{1,2})\s*点(?:\s*(半|一刻|三刻|\d{1,2})\s*分?)?");
  if(m.Success){int hour=Int32.Parse(m.Groups[2].Value),minute=0;string min=m.Groups[3].Value;if(min=="半")minute=30;else if(min=="一刻")minute=15;else if(min=="三刻")minute=45;else if(min.Length>0)Int32.TryParse(min,out minute);if((m.Groups[1].Value=="下午"||m.Groups[1].Value=="晚上"||m.Groups[1].Value=="中午")&&hour<12)hour+=12;if(hour<24&&minute<60){d.Time=hour.ToString("00")+":"+minute.ToString("00");d.HasDate=true;text=text.Remove(m.Index,m.Length);}}
  m=Regex.Match(text,@"每个工作日|每工作日|每周[一二三四五六日天]|每天|每周|每月最后一天|每月|每年");if(m.Success){string token=m.Value;d.Repeat=token.Contains("工作日")?"工作日":token.StartsWith("每周")?"每周":token;if(token.StartsWith("每周")&&token.Length==3){int index="一二三四五六日".IndexOf(token[2]);if(index<0)index=6;d.Repeat="自定义周";d.Weekdays=(index+1).ToString();date=today.AddDays((index-(((int)today.DayOfWeek+6)%7)+7)%7);}d.HasDate=true;text=text.Remove(m.Index,m.Length);}
  if(text.Contains("#工作")){d.Category="工作";text=text.Replace("#工作","");}
  d.Date=date.ToString("yyyy-MM-dd");d.Title=Regex.Replace(text,@"\s+"," ").Trim(' ','，',',','。');return d;
 }
 static int ChineseNumber(string text){if(text=="两")return 2;int ten=text.IndexOf('十');string digits="零一二三四五六七八九";if(ten<0)return Math.Max(0,digits.IndexOf(text[0]));return (ten==0?1:digits.IndexOf(text[0]))*10+(ten+1<text.Length?digits.IndexOf(text[ten+1]):0);}
}
