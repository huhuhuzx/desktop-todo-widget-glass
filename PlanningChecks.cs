using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class PlanningChecks
{
 static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 static TaskItem Repeating(string date,string repeat){var task=new TaskItem{Title="Synthetic task",DueDate=date,Repeat=repeat};PlanningRules.Normalize(task);return task;}
 public static int Run(){
  string directory=Path.Combine(Path.GetTempPath(),"LumiPlanningChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  try{
   var json=new JavaScriptSerializer();TaskItem legacy=json.Deserialize<TaskItem>("{\"Title\":\"Legacy\",\"IsDone\":true,\"DueDate\":\"2026-01-31\"}");PlanningRules.Normalize(legacy);Require(legacy.IsDone&&!legacy.IsAwaitingReview&&legacy.Checklist.Count==0,"Legacy migration");
   var monthly=Repeating("2026-01-31","每月");Require(PlanningRules.NextDate(monthly,new DateTime(2026,2,5))==new DateTime(2026,2,28),"January month end");
   var feb=PlanningRules.CreateNext(monthly,new DateTime(2026,2,5));Require(PlanningRules.NextDate(feb,new DateTime(2026,3,1))==new DateTime(2026,3,31),"Month end must not drift to 28");
   var last=Repeating("2028-01-31","每月最后一天");Require(PlanningRules.NextDate(last,DateTime.Today)==new DateTime(2028,2,29),"Leap year last day");
   var business=Repeating("2026-11-06","工作日");Require(PlanningRules.NextDate(business,DateTime.Today)==new DateTime(2026,11,9),"Business Friday to Monday");business.RepeatInterval=2;Require(PlanningRules.NextDate(business,DateTime.Today)==new DateTime(2026,11,10),"Business interval");
   var weekly=Repeating("2026-11-02","自定义周");weekly.RepeatInterval=2;weekly.RepeatWeekdays="1,3";Require(PlanningRules.NextDate(weekly,DateTime.Today)==new DateTime(2026,11,4),"Selected weekdays");weekly.DueDate="2026-11-04";Require(PlanningRules.NextDate(weekly,DateTime.Today)==new DateTime(2026,11,16),"Alternate weeks");
   var completed=Repeating("2026-11-02","每周");completed.RepeatFromCompletion=true;Require(PlanningRules.NextDate(completed,new DateTime(2026,11,9))==new DateTime(2026,11,16),"Completion anchor");completed.RepeatFromCompletion=false;Require(PlanningRules.NextDate(completed,new DateTime(2026,11,9))==new DateTime(2026,11,9),"Scheduled anchor");
   var except=Repeating("2026-11-02","每天");except.RepeatExceptions="2026-11-03,2026-11-04";Require(PlanningRules.NextDate(except,DateTime.Today)==new DateTime(2026,11,5),"Skipped dates");except.RepeatUntil="2026-11-04";Require(!PlanningRules.NextDate(except,DateTime.Today).HasValue,"Repeat until");except.RepeatUntil="";except.RepeatLimit=1;Require(!PlanningRules.NextDate(except,DateTime.Today).HasValue,"Repeat count includes original");
   var source=Repeating("2026-11-02","每周");source.EndDate="2026-11-04";source.DeadlineDate="2026-11-05";source.Checklist.Add(new ChecklistItem{Text="Step",Done=true});source.SubmittedAt="2026-11-02";source.IsDone=true;var next=PlanningRules.CreateNext(source,new DateTime(2026,11,3));Require(next.DueDate=="2026-11-09"&&next.EndDate=="2026-11-11"&&next.DeadlineDate=="2026-11-12"&&!next.IsDone&&!next.IsAwaitingReview&&!next.Checklist[0].Done&&String.IsNullOrEmpty(next.SubmittedAt),"Next instance reset and date gaps");
   Require(PlanningRules.Occurs(source,new DateTime(2026,11,3))&&!PlanningRules.Occurs(source,new DateTime(2026,11,5)),"Multi-day range");source.DeletedAt=DateTime.Now.ToString("o");Require(!PlanningRules.Occurs(source,new DateTime(2026,11,3)),"Deleted calendar exclusion");
   var draft=QuickDraft.Parse("明天下午三点 提交方案 #工作",new DateTime(2026,11,9));Require(draft.Title=="提交方案"&&draft.Date=="2026-11-10"&&draft.Time=="15:00"&&draft.Category=="工作","Chinese quick capture");
   draft=QuickDraft.Parse("每周三 09:15 例会",new DateTime(2026,11,9));Require(draft.Repeat=="自定义周"&&draft.Weekdays=="3"&&draft.Date=="2026-11-11"&&draft.Title=="例会","Weekly quick capture");
   draft=QuickDraft.Parse("以后再考虑的想法",new DateTime(2026,11,9));Require(!draft.HasDate&&draft.Title=="以后再考虑的想法","Inbox capture");
   var history=new PlanningHistory();history.Reset("before");history.Record("after","edit");Require(history.Undo()=="before"&&history.Redo()=="after","Undo and redo");history.Undo();history.Record("branch","new edit");Require(!history.CanRedo,"Redo invalidated by new edit");
   string path=Path.Combine(directory,"tasks.json");TaskStorage.Write(path,new[]{new TaskItem{Title="Before"}});string backup=BackupVault.Capture(path,true);TaskStorage.Write(path,new[]{new TaskItem{Title="After"}});Require(TaskStorage.Read(backup).Single().Title=="Before"&&TaskStorage.Read(path).Single().Title=="After","Backup restoration contents");
   string malformed="[{\"Title\":\"keep\"},null]";File.WriteAllText(path,malformed);bool rejected=false;try{TaskStorage.Read(path);}catch{rejected=true;}Require(rejected&&File.ReadAllText(path)==malformed,"Invalid import must not mutate source");
   return 0;
  }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"planning-checks.log"),ex.ToString());return 81;}
  finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
 }
}

internal sealed partial class DesktopTodoApp
{
 void RunWorkspaceChecks(object sender,EventArgs e){
  window.ContentRendered-=RunWorkspaceChecks;
  try{
   ReplaceInMemory(new List<TaskItem>());history.Reset(SerializeTasks());
   ShowEditor(null);editTitle.Text="Synthetic review plan";editDate.SelectedDate=DateTime.Today;editorEnd.SelectedDate=DateTime.Today.AddDays(2);editorDeadline.SelectedDate=DateTime.Today.AddDays(4);editorReviewer.Text="审核人";editorFollowUp.SelectedDate=DateTime.Today.AddDays(1);editorProject.Text="演示项目";editorTags.Text="方案,本周";editorPriority.SelectedIndex=3;editorEstimate.Text="45";editorChecklist.Text="[x] 已准备\n[ ] 待核对";Select(editRepeat,"每周");SaveEditor();
   TaskItem task=tasks.Single();if(task.Checklist.Count!=2||!task.Checklist[0].Done||task.Priority!=3||task.Project!="演示项目")throw new Exception("Editor extended fields did not save");
   foreach(string exact in new[]{"09:07","09:59"}){task.DueTime=exact;ShowEditor(task);editTitle.Text="Synthetic review plan";SaveEditor();if(task.DueTime!=exact)throw new Exception("Minute precision lost: "+exact);}
   ShowDetails(task);Find<Button>("DetailReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(mode!="Review"||!task.IsAwaitingReview||tasks.Count!=1||String.IsNullOrEmpty(task.ExecutedAt)||!String.IsNullOrEmpty(task.CompletedAt))throw new Exception("Review transition");
   ShowDetails(task);Find<Button>("DetailDone").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(tasks.Count!=2||!task.IsDone||String.IsNullOrEmpty(task.CompletedAt))throw new Exception("Confirmed recurrence");
   Next(task);if(tasks.Count!=2)throw new Exception("Duplicate recurrence");
   DeleteOrRestore(task);SetMode("Recycle");if(!view.Cast<object>().Contains(task))throw new Exception("Recycle filter");ReplayHistory(false);if(tasks.Any(x=>x.Id==task.Id&&x.IsDeleted))throw new Exception("Delete undo");ReplayHistory(true);if(!tasks.Any(x=>x.Id==task.Id&&x.IsDeleted))throw new Exception("Delete redo");task=tasks.Single(x=>x.Id==task.Id);DeleteOrRestore(task);
   ShowQuickAdd();var input=VisualChild<TextBox>(auxBody);input.Text="明天下午三点 提交材料 #工作\n待整理的想法";var inbox=VisualChild<CheckBox>(auxBody);inbox.IsChecked=false;var preview=auxBody.Children.OfType<StackPanel>().Single();if(!preview.Children.OfType<TextBlock>().Last().Text.Contains(DateTime.Today.ToString("yyyy-MM-dd"))||preview.Children.OfType<TextBlock>().Last().Text.Contains("收集箱"))throw new Exception("Quick preview does not follow inbox choice");inbox.IsChecked=true;((Button)auxActions.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!tasks.Any(x=>x.Title=="提交材料"&&x.DueTime=="15:00")||!tasks.Any(x=>x.Title=="待整理的想法"&&x.Unscheduled))throw new Exception("Quick capture UI");
   SetMode("Week");RenderWeek();if(weekPanel.Visibility!=Visibility.Visible||((Grid)weekBody.Children[0]).Children.Count!=7)throw new Exception("Week view");
   searchBox.Text="___no_such_task___";if(WeekTaskCount()!=0)throw new Exception("Week search did not refresh");searchBox.Text="";if(WeekTaskCount()==0)throw new Exception("Week search did not clear");activeFilter=new SavedTaskFilter{Project="___no_such_project___"};RefreshWorkspace();if(WeekTaskCount()!=0)throw new Exception("Week project filter ignored");activeFilter=null;RefreshWorkspace();if(WeekTaskCount()==0)throw new Exception("Week filter did not clear");
   SetMode("Completed");SetMode("Review");SetMode("Calendar");SetMode("Agenda");if(calendarPanel.Visibility==Visibility.Visible||weekPanel.Visibility==Visibility.Visible||agendaPanel.Visibility!=Visibility.Visible)throw new Exception("Navigation panel visibility");
   Environment.ExitCode=0;
  }catch(Exception ex){Log(ex);Environment.ExitCode=82;}
  window.Close();
 }
 int WeekTaskCount(){return ((Grid)weekBody.Children[0]).Children.OfType<Border>().Sum(x=>((StackPanel)x.Child).Children.OfType<Button>().Count());}
}
