using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path=System.IO.Path;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

[assembly:AssemblyTitle("流光日程")]
[assembly:AssemblyDescription("原生 Windows 桌面日程与待办小组件")]
[assembly:AssemblyProduct("流光日程 · 玻璃版")]
[assembly:AssemblyVersion("2.4.0.0")]
[assembly:AssemblyFileVersion("2.4.0.0")]

public sealed class TaskItem:INotifyPropertyChanged
{
 internal static Brush PersonalCategoryBrush=new SolidColorBrush(Color.FromRgb(40,108,145));
 string id,title,dueDate,dueTime,category,repeat,location,notes,notified;bool isDone,recurrenceCreated;int reminder;
 public TaskItem(){id=Guid.NewGuid().ToString();title="";dueDate=DateTime.Today.ToString("yyyy-MM-dd");dueTime="";category="个人";repeat="不重复";location=notes=notified="";reminder=-1;}
 public string Id{get{return id;}set{Set(ref id,value);}}public string Title{get{return title;}set{Set(ref title,value??"");}}public bool IsDone{get{return isDone;}set{Set(ref isDone,value);}}
 public string DueDate{get{return dueDate;}set{Set(ref dueDate,value??"");Notify("Meta");}}public string DueTime{get{return dueTime;}set{Set(ref dueTime,value??"");Notify("Meta");}}
 public string Category{get{return category;}set{Set(ref category,value??"个人");Notify("Meta");Notify("CategoryBrush");}}public string Repeat{get{return repeat;}set{Set(ref repeat,value??"不重复");Notify("Meta");}}
 public int Reminder{get{return reminder;}set{Set(ref reminder,value);}}public string Location{get{return location;}set{Set(ref location,value??"");Notify("Meta");}}public string Notes{get{return notes;}set{Set(ref notes,value??"");}}
 public string Notified{get{return notified;}set{Set(ref notified,value??"");}}public bool RecurrenceCreated{get{return recurrenceCreated;}set{Set(ref recurrenceCreated,value);}}
 [ScriptIgnore]public Brush CategoryBrush{get{if(Category!="工作"&&Category!="生活"&&Category!="重要")return PersonalCategoryBrush;string c=Category=="工作"?"#607FF2":Category=="生活"?"#46B58B":"#F06D6D";return new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));}}
 public void RefreshCategoryBrush(){Notify("CategoryBrush");}
 [ScriptIgnore]public string Meta{get{var p=new List<string>();DateTime d;if(DateTime.TryParseExact(DueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d))p.Add(d.ToString("M月d日 ddd",CultureInfo.GetCultureInfo("zh-CN")));if(!String.IsNullOrEmpty(DueTime))p.Add(DueTime);if(!String.IsNullOrEmpty(Category))p.Add(Category);if(!String.IsNullOrEmpty(Location))p.Add(Location);if(!String.IsNullOrEmpty(Repeat)&&Repeat!="不重复")p.Add(Repeat);return String.Join(" · ",p);}}
 public event PropertyChangedEventHandler PropertyChanged;void Notify(string n){var h=PropertyChanged;if(h!=null)h(this,new PropertyChangedEventArgs(n));}void Set<T>(ref T f,T v,[CallerMemberName]string n=null){if(EqualityComparer<T>.Default.Equals(f,v))return;f=v;Notify(n);}
}
public sealed class WindowSettings{public double Left{get;set;}public double Top{get;set;}public double Width{get;set;}public double Height{get;set;}public bool Topmost{get;set;}public string Theme{get;set;}public double Opacity{get;set;}public string AccentColor{get;set;}}
public sealed class DayEntry{public string Display{get;set;}public TaskItem Task{get;set;}}

internal static class TaskStorage
{
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 const string FileMutexName=@"Local\DesktopTodoWidget_Glass_TaskFile_1";
 public static List<TaskItem> Read(string path){
  var result=new List<TaskItem>();if(!File.Exists(path))return result;
  object raw=Json.DeserializeObject(File.ReadAllText(path,Encoding.UTF8));
  object[] items=raw as object[];
  if(items!=null){foreach(object item in items){TaskItem task=Json.ConvertToType<TaskItem>(item);if(task==null)throw new InvalidDataException("任务列表包含空条目");Normalize(task);result.Add(task);}}
  else if(raw is Dictionary<string,object>){TaskItem task=Json.ConvertToType<TaskItem>(raw);Normalize(task);result.Add(task);}
  else throw new InvalidDataException("任务文件不是任务列表或任务对象");
  return result;
 }
 public static void Write(string path,IEnumerable<TaskItem> tasks){
  string temporary=path+".tmp";
  File.WriteAllText(temporary,Json.Serialize(tasks.ToList()),new UTF8Encoding(false));
  if(File.Exists(path))File.Replace(temporary,path,null);
  else File.Move(temporary,path);
 }
 public static void Save(string path,IEnumerable<TaskItem> tasks){
  using(var mutex=new Mutex(false,FileMutexName)){
   bool acquired=false;try{
    try{acquired=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){acquired=true;}
    if(!acquired)throw new TimeoutException("等待任务文件锁超时");
    var list=tasks.ToList();var disk=Read(path).GroupBy(t=>t.Id).ToDictionary(g=>g.Key,g=>g.First());
    foreach(TaskItem item in list){TaskItem previous;if(disk.TryGetValue(item.Id,out previous)&&previous.DueDate==item.DueDate&&previous.DueTime==item.DueTime&&previous.Reminder==item.Reminder&&!String.IsNullOrEmpty(previous.Notified))item.Notified=previous.Notified;}
    Write(path,list);
   }finally{if(acquired)mutex.ReleaseMutex();}
  }
 }
 public static TaskItem ClaimReminder(string path,string id,DateTime now){
  using(var mutex=new Mutex(false,FileMutexName)){
   bool acquired=false;try{
    try{acquired=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){acquired=true;}
    if(!acquired)throw new TimeoutException("等待任务文件锁超时");
    var list=Read(path);TaskItem task=list.FirstOrDefault(t=>t.Id==id);
    if(task==null||task.IsDone||task.Reminder<0||String.IsNullOrEmpty(task.DueTime))return null;
    DateTime due;if(!DateTime.TryParseExact(task.DueDate+" "+task.DueTime,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out due))return null;
    string key=due.ToString("s",CultureInfo.InvariantCulture);
    if(now<due.AddMinutes(-task.Reminder)||now-due>=TimeSpan.FromHours(24)||task.Notified==key)return null;
    task.Notified=key;Write(path,list);return task;
   }finally{if(acquired)mutex.ReleaseMutex();}
  }
 }
 static void Normalize(TaskItem t){if(String.IsNullOrEmpty(t.Id))t.Id=Guid.NewGuid().ToString();if(t.Title==null)t.Title="";if(String.IsNullOrEmpty(t.DueDate))t.DueDate=DateTime.Today.ToString("yyyy-MM-dd");if(String.IsNullOrEmpty(t.Category))t.Category="个人";if(String.IsNullOrEmpty(t.Repeat))t.Repeat="不重复";if(t.DueTime==null)t.DueTime="";if(t.Location==null)t.Location="";if(t.Notes==null)t.Notes="";if(t.Notified==null)t.Notified="";}
}

internal sealed class DesktopTodoApp
{
 readonly string folder,taskPath,settingsPath,logPath;readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly bool selfTest,hoverTest,uiTest,previewTest;
 Grid tabRailInner;Border activeTabPill,overlayScrim;Thumb opacityThumb;int lastOpacityReadout=-1,accentPickerVersion,scrimVersion;bool accentPickerOpen;DateTime lastOpacityMotion=DateTime.MinValue;string pillMode="";
 Window window;ObservableCollection<TaskItem> tasks;ICollectionView view;string mode="Agenda";bool loading,editing,suppressCalendarMouseUp;TaskItem editingTask;
 Forms.NotifyIcon notifyIcon;DispatcherTimer reminderTimer,settingsTimer;
 Grid agendaPanel,calendarPanel;Border editorPanel,detailsPanel,settingsPanel,todayBadge,titleInputBorder,successToast,accentPickerPanel;ListBox taskList,dayList;TextBlock searchHint,headerTitle,emptyTitle,todayBadgeText,editorError,opacityValue,detailTitle,detailStatus,detailDateTime,detailCategoryRepeat,detailReminder,detailLocation,detailNotes,selectedDayHeading,calendarNavHint,accentColorLabel,accentInputHint;TextBox searchBox,editTitle,editLocation,editNotes,accentHexInput;StackPanel emptyState,pendingDigits,totalDigits,tabStrip,accentPalette;Slider opacitySlider;TaskItem detailsTask;System.Windows.Shapes.Path successPath;DispatcherTimer errorTimer,successTimer;int pageVersion,successVersion,errorVersion,lastPending=-1,lastTotal=-1,lastToday=-1,hourIndex,minuteIndex;string lastRevealedDay="";Color accentColor=Color.FromRgb(40,108,145);readonly Dictionary<UIElement,int> overlayVersions=new Dictionary<UIElement,int>();readonly Dictionary<TextBlock,int> textVersions=new Dictionary<TextBlock,int>();readonly HashSet<Border> dropdownHooks=new HashSet<Border>();Button dateTodayButton;
 System.Windows.Controls.Calendar monthCalendar;DatePicker editDate;Button editHour,editMinute,accentColorButton;ComboBox editCategory,editRepeat,editReminder,themeSelect;Button agendaTab,todayTab,calendarTab,completedTab,pinButton;
 public DesktopTodoApp(bool test,bool hover=false,bool ui=false,bool preview=false){selfTest=test;hoverTest=hover;uiTest=ui;previewTest=preview;folder=AppDomain.CurrentDomain.BaseDirectory;taskPath=Path.Combine(folder,"tasks.json");settingsPath=Path.Combine(folder,"widget-settings.json");logPath=Path.Combine(folder,"流光日程-错误日志.txt");}
 T Find<T>(string n)where T:class{T v=window.FindName(n)as T;if(v==null)throw new InvalidOperationException("找不到控件："+n);return v;}
 public void Run(){using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("UI.xaml")){if(s==null)throw new InvalidOperationException("找不到 UI.xaml");window=(Window)XamlReader.Load(s);}Resolve();ResolveGlass();LoadSettings();if(previewTest)LoadPreviewTasks();else LoadTasks();Bind();Wire();StartNotifications();UpdateTabs();RefreshDay();UpdateSummary();if(previewTest)window.ContentRendered+=RunPreview;else if(uiTest)window.ContentRendered+=RunUiTest;else if(hoverTest)window.ContentRendered+=RunHoverTest;else if(selfTest)window.ContentRendered+=RunSelfTest;var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};app.DispatcherUnhandledException+=delegate(object o,DispatcherUnhandledExceptionEventArgs e){Log(e.Exception);e.Handled=true;MessageBox.Show("操作失败，程序已保护性恢复。详情见错误日志。","流光日程");};app.Run(window);}
 void LoadPreviewTasks(){string day=DateTime.Today.ToString("yyyy-MM-dd");tasks=new ObservableCollection<TaskItem>{new TaskItem{Title="晨间计划与本周重点",DueDate=day,DueTime="09:00",Category="工作"},new TaskItem{Title="整理产品方案与界面反馈",DueDate=day,DueTime="14:30",Category="个人"},new TaskItem{Title="散步与阅读",DueDate=day,DueTime="19:00",Category="生活"}};}
 void RunPreview(object sender,EventArgs e){
  window.ContentRendered-=RunPreview;
  try{SavePreview("light-home");themeSelect.SelectedIndex=1;ApplyAppearance();OpenOverlay(settingsPanel,true);RunLater(300,delegate{try{SavePreview("dark-settings");window.Width=380;window.Height=540;window.UpdateLayout();SavePreview("compact-settings");Environment.ExitCode=0;}catch(Exception ex){Log(ex);Environment.ExitCode=53;}window.Close();});}
  catch(Exception ex){Log(ex);Environment.ExitCode=53;window.Close();}
 }
 void SavePreview(string name){
  FrameworkElement root=(FrameworkElement)window.Content;root.UpdateLayout();int width=(int)Math.Ceiling(root.ActualWidth),height=(int)Math.Ceiling(root.ActualHeight);
  var content=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);content.Render(root);
  var visual=new DrawingVisual();using(DrawingContext dc=visual.RenderOpen()){dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(223,213,208),Color.FromRgb(123,151,177),new Point(0,0),new Point(1,1)),null,new Rect(0,0,width,height));dc.DrawImage(content,new Rect(0,0,width,height));}
  var output=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);output.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(output));string preview=Path.Combine(folder,"preview");Directory.CreateDirectory(preview);using(Stream stream=File.Create(Path.Combine(preview,name+".png")))encoder.Save(stream);
 }
 void RunUiTest(object sender,EventArgs e){
  window.ContentRendered-=RunUiTest;SetMode("Today");bool blurred=headerTitle.Effect is BlurEffect;ShowEditor(null);
  bool tall=editHour.Height>44||editMinute.Height>44;
  IntPtr probe=CreateRectRgn(0,0,1,1);int shape=GetWindowRgn(new WindowInteropHelper(window).Handle,probe);DeleteObject(probe);bool corners=window.AllowsTransparency&&shape==0&&window.Background==Brushes.Transparent&&CornerPixelsAreTransparent();
  editDate.SelectedDate=DateTime.Today.AddDays(1);editDate.IsDropDownOpen=true;
  RunLater(100,delegate{
   Popup popup=editDate.Template.FindName("PART_Popup",editDate) as Popup;
   Border shell=popup==null?null:popup.Child as Border;
   var calendar=popup==null?null:VisualChild<System.Windows.Controls.Calendar>(popup.Child);
   bool styled=shell!=null&&shell.Tag as string=="StyledDatePopup"&&calendar!=null;
   bool headerWorks=false;
   if(calendar!=null){calendar.ApplyTemplate();calendar.UpdateLayout();var item=calendar.Template.FindName("PART_CalendarItem",calendar) as CalendarItem;if(item!=null){item.ApplyTemplate();Button header=item.Template.FindName("PART_HeaderButton",item) as Button;if(header!=null){header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));headerWorks=calendar.DisplayMode==CalendarMode.Year;calendar.DisplayMode=CalendarMode.Month;}}}
   if(calendar!=null)calendar.SelectedDate=DateTime.Today.AddDays(2);
   bool selected=editDate.SelectedDate==DateTime.Today.AddDays(2);
   Button today=dateTodayButton;
   if(today!=null)today.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   accentColor=Color.FromRgb(255,220,80);themeSelect.SelectedIndex=1;ApplyAppearance();bool contrast=TextContrastValid();
   themeSelect.SelectedIndex=0;ApplyAppearance();contrast=contrast&&TextContrastValid();
   accentColorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));accentHexInput.Text="#20C0A0";Find<Button>("ApplyAccentButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   bool picker=accentPickerPanel.Visibility==Visibility.Visible&&accentColor==Color.FromRgb(32,192,160)&&accentColorLabel.Text.Contains("#20C0A0")&&accentPalette.Children.Count==6&&((SolidColorBrush)window.Resources["AccentBadgeBrush"]).Color==accentColor&&window.Resources["SurfaceBrush"] is LinearGradientBrush;
   opacitySlider.Value=75;AttachOpacityThumb();MoveTabPill(false);double pillX=todayTab.TransformToAncestor(tabRailInner).Transform(new Point(0,0)).X;
   bool readout=opacityValue.Text=="75%"&&opacityThumb!=null,ambient=window.Resources["GlassAmbientBrush"] is RadialGradientBrush&&window.Resources["ContentWellBrush"] is SolidColorBrush,pill=Math.Abs(Motion.Translate(activeTabPill).X-pillX)<.5&&activeTabPill.Width>0;
   Environment.ExitCode=blurred?41:tall?42:!corners?43:!styled?44:!headerWorks?50:!selected?49:today==null?46:editDate.SelectedDate!=DateTime.Today?47:editDate.IsDropDownOpen?48:!contrast?45:!picker?51:!readout?52:!ambient?53:!pill?54:0;window.Close();
  });
 }
 void Resolve(){agendaPanel=Find<Grid>("AgendaPanel");calendarPanel=Find<Grid>("CalendarPanel");editorPanel=Find<Border>("EditorPanel");detailsPanel=Find<Border>("DetailsPanel");settingsPanel=Find<Border>("SettingsPanel");todayBadge=Find<Border>("TodayBadge");todayBadgeText=Find<TextBlock>("TodayBadgeText");titleInputBorder=Find<Border>("TitleInputBorder");editorError=Find<TextBlock>("EditorError");successToast=Find<Border>("SuccessToast");successPath=Find<System.Windows.Shapes.Path>("SuccessPath");taskList=Find<ListBox>("TaskList");dayList=Find<ListBox>("DayList");selectedDayHeading=Find<TextBlock>("SelectedDayHeading");calendarNavHint=Find<TextBlock>("CalendarNavHint");searchBox=Find<TextBox>("SearchBox");searchHint=Find<TextBlock>("SearchHint");headerTitle=Find<TextBlock>("HeaderTitle");emptyTitle=Find<TextBlock>("EmptyTitle");pendingDigits=Find<StackPanel>("PendingDigits");totalDigits=Find<StackPanel>("TotalDigits");tabStrip=Find<StackPanel>("TabStrip");emptyState=Find<StackPanel>("EmptyState");monthCalendar=Find<System.Windows.Controls.Calendar>("MonthCalendar");editDate=Find<DatePicker>("EditDate");editTitle=Find<TextBox>("EditTitle");editHour=Find<Button>("EditHour");editMinute=Find<Button>("EditMinute");accentColorButton=Find<Button>("AccentColorButton");accentColorLabel=Find<TextBlock>("AccentColorLabel");accentPickerPanel=Find<Border>("AccentPickerPanel");accentPalette=Find<StackPanel>("AccentPalette");accentHexInput=Find<TextBox>("AccentHexInput");accentInputHint=Find<TextBlock>("AccentInputHint");editLocation=Find<TextBox>("EditLocation");editNotes=Find<TextBox>("EditNotes");editCategory=Find<ComboBox>("EditCategory");editRepeat=Find<ComboBox>("EditRepeat");editReminder=Find<ComboBox>("EditReminder");themeSelect=Find<ComboBox>("ThemeSelect");opacitySlider=Find<Slider>("OpacitySlider");opacityValue=Find<TextBlock>("OpacityValue");detailTitle=Find<TextBlock>("DetailTitle");detailStatus=Find<TextBlock>("DetailStatus");detailDateTime=Find<TextBlock>("DetailDateTime");detailCategoryRepeat=Find<TextBlock>("DetailCategoryRepeat");detailReminder=Find<TextBlock>("DetailReminder");detailLocation=Find<TextBlock>("DetailLocation");detailNotes=Find<TextBlock>("DetailNotes");agendaTab=Find<Button>("AgendaTab");todayTab=Find<Button>("TodayTab");calendarTab=Find<Button>("CalendarTab");completedTab=Find<Button>("CompletedTab");pinButton=Find<Button>("PinButton");Find<TextBlock>("DateText").Text=DateTime.Now.ToString("M月d日 dddd",CultureInfo.GetCultureInfo("zh-CN"));monthCalendar.SelectedDate=DateTime.Today;BuildTimes();BuildAccentPalette();window.Resources["GlassNoiseBrush"]=CreateGlassNoiseBrush();}
 void ResolveGlass(){tabRailInner=Find<Grid>("TabRailInner");activeTabPill=Find<Border>("ActiveTabPill");overlayScrim=Find<Border>("OverlayScrim");}
 void MoveTabPill(bool animate){
  if(tabRailInner==null||!tabRailInner.IsLoaded)return;
  Button selected=mode=="Today"?todayTab:mode=="Calendar"?calendarTab:mode=="Completed"?completedTab:agendaTab;
  if(selected.ActualWidth<1)return;
  double x=selected.TransformToAncestor(tabRailInner).Transform(new Point(0,0)).X,width=selected.ActualWidth;
  TranslateTransform shift=Motion.Translate(activeTabPill);
  if(!animate||pillMode==""||!Motion.Enabled){shift.BeginAnimation(TranslateTransform.XProperty,null);activeTabPill.BeginAnimation(FrameworkElement.WidthProperty,null);shift.X=x;activeTabPill.Width=width;activeTabPill.Height=selected.ActualHeight;}
  else{Motion.Tween(shift,TranslateTransform.XProperty,shift.X,x,Motion.Fast);Motion.Tween(activeTabPill,FrameworkElement.WidthProperty,activeTabPill.Width,width,Motion.Fast);}
  pillMode=mode;
 }
 void UpdateOpacityReadout(){
  int value=(int)Math.Round(opacitySlider.Value);if(value==lastOpacityReadout)return;
  bool animate=lastOpacityReadout>=0&&opacityValue.IsLoaded&&Motion.Enabled&&(DateTime.UtcNow-lastOpacityMotion).TotalMilliseconds>=130;
  lastOpacityReadout=value;opacityValue.Text=value.ToString(CultureInfo.InvariantCulture)+"%";
  TranslateTransform shift=Motion.Translate(opacityValue);
  if(animate){lastOpacityMotion=DateTime.UtcNow;Motion.Tween(shift,TranslateTransform.YProperty,4,0,Motion.Quick);Motion.Tween(opacityValue,UIElement.OpacityProperty,.7,1,Motion.Quick);}
  else{shift.BeginAnimation(TranslateTransform.YProperty,null);shift.Y=0;opacityValue.BeginAnimation(UIElement.OpacityProperty,null);opacityValue.Opacity=1;}
 }
 void AttachOpacityThumb(){if(opacityThumb!=null)return;opacitySlider.ApplyTemplate();opacityThumb=VisualChild<Thumb>(opacitySlider);if(opacityThumb==null)return;opacityThumb.DragStarted+=delegate{ScaleOpacityThumb(1.13);};opacityThumb.DragCompleted+=delegate{ScaleOpacityThumb(1);};}
 void ScaleOpacityThumb(double target){if(opacityThumb==null)return;ScaleTransform scale=Motion.Scale(opacityThumb);Motion.Tween(scale,ScaleTransform.ScaleXProperty,scale.ScaleX,target,Motion.Quick);Motion.Tween(scale,ScaleTransform.ScaleYProperty,scale.ScaleY,target,Motion.Quick);}
 static Brush CreateGlassNoiseBrush(){var random=new Random(20260916);byte[] pixels=new byte[64*64*4];for(int i=0;i<pixels.Length;i+=4){byte tone=(byte)(random.Next(2)==0?0:255);pixels[i]=pixels[i+1]=pixels[i+2]=tone;pixels[i+3]=8;}var bitmap=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,64*4);bitmap.Freeze();var brush=new ImageBrush(bitmap){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,64,64),Stretch=Stretch.None};brush.Freeze();return brush;}
 void BuildTimes(){SetTimeValue(false,0,false);SetTimeValue(true,0,false);foreach(Button wheel in new[]{editHour,editMinute}){wheel.PreviewMouseWheel+=ScrollTimeWheel;wheel.Click+=delegate(object sender,RoutedEventArgs e){StepTime((Button)sender,1);};wheel.PreviewMouseRightButtonUp+=delegate(object sender,MouseButtonEventArgs e){StepTime((Button)sender,-1);e.Handled=true;};wheel.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Up||e.Key==Key.Down){StepTime((Button)sender,e.Key==Key.Up?1:-1);e.Handled=true;}};}}
 bool CornerPixelsAreTransparent(){
  FrameworkElement root=window.Content as FrameworkElement;if(root==null)return false;root.UpdateLayout();int width=(int)Math.Ceiling(root.ActualWidth),height=(int)Math.Ceiling(root.ActualHeight);if(width<44||height<44)return false;
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);byte[] pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
  Func<int,int,byte> alpha=(x,y)=>pixels[(y*width+x)*4+3];return alpha(0,0)<=2&&alpha(width-1,0)<=2&&alpha(0,height-1)<=2&&alpha(width-1,height-1)<=2&&alpha(width/2,height/2)>100;
 }
 void BuildAccentPalette(){
  foreach(string hex in new[]{"#286C91","#6576C7","#AD6FA0","#BC7858","#438C73","#577AA3"}){
   Color swatch=(Color)ColorConverter.ConvertFromString(hex);
   var button=new Button{Background=new SolidColorBrush(swatch),BorderBrush=(Brush)window.FindResource("BorderBrush"),Style=(Style)window.FindResource("PaletteButton"),Tag=swatch,ToolTip=hex};
   button.Click+=delegate{accentColor=(Color)button.Tag;accentHexInput.Text=AccentHex();ApplyAppearance();QueueSettings();};accentPalette.Children.Add(button);
  }
 }
 string AccentHex(){return String.Format("#{0:X2}{1:X2}{2:X2}",accentColor.R,accentColor.G,accentColor.B);}
 void ApplyAccentInput(){Color parsed;if(!TryAccent((accentHexInput.Text??"").Trim(),out parsed)){accentInputHint.Text="请输入 #RRGGBB 格式的颜色";accentInputHint.SetResourceReference(TextBlock.ForegroundProperty,"ErrorBrush");return;}accentColor=parsed;ApplyAppearance();QueueSettings();}
 void ScrollTimeWheel(object sender,MouseWheelEventArgs e){StepTime((Button)sender,e.Delta>0?-1:1);e.Handled=true;}
 void StepTime(Button wheel,int step){if(wheel==editMinute&&hourIndex==0)return;bool minute=wheel==editMinute;int count=minute?4:25;int index=(minute?minuteIndex:hourIndex)+step;SetTimeValue(minute,(index+count)%count,true);}
 void SetTimeValue(bool minute,int index,bool animate){Button wheel=minute?editMinute:editHour;if(minute)minuteIndex=index;else hourIndex=index;string next=minute?(hourIndex==0?"—":(index*15).ToString("00")):index==0?"全天":(index-1).ToString("00");if(!minute){editMinute.IsEnabled=index>0;editMinute.Content=index>0?(minuteIndex*15).ToString("00"):"—";}if(Convert.ToString(wheel.Content)==next)return;wheel.Content=next;if(animate&&Motion.Enabled){wheel.ApplyTemplate();ContentPresenter content=VisualChild<ContentPresenter>(wheel);if(content!=null)Motion.Enter(content,150,0,4,1,0);}}
 static T VisualChild<T>(DependencyObject parent)where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){DependencyObject child=VisualTreeHelper.GetChild(parent,i);T match=child as T??VisualChild<T>(child);if(match!=null)return match;}return null;}
 void LoadTasks(){loading=true;try{tasks=new ObservableCollection<TaskItem>(TaskStorage.Read(taskPath));foreach(TaskItem t in tasks)t.PropertyChanged+=Changed;}catch(Exception ex){Log(ex);throw new InvalidDataException("无法读取 tasks.json；为避免覆盖原数据，程序已停止启动。请先备份并检查该文件。",ex);}finally{loading=false;}}
 void Bind(){view=CollectionViewSource.GetDefaultView(tasks);view.Filter=Filter;view.SortDescriptions.Add(new SortDescription("DueDate",ListSortDirection.Ascending));view.SortDescriptions.Add(new SortDescription("DueTime",ListSortDirection.Ascending));taskList.ItemsSource=view;}
 bool Filter(object o){TaskItem t=o as TaskItem;if(t==null)return false;string q=(searchBox.Text??"").Trim();bool s=q.Length==0||Has(t.Title,q)||Has(t.Notes,q)||Has(t.Location,q);bool state=mode=="Today"?!t.IsDone&&t.DueDate==DateTime.Today.ToString("yyyy-MM-dd"):mode=="Completed"?t.IsDone:!t.IsDone;return s&&state;}static bool Has(string s,string q){return(s??"").IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0;}
 void Changed(object sender,PropertyChangedEventArgs e){if(loading||editing)return;TaskItem t=(TaskItem)sender;if(e.PropertyName=="IsDone"){if(t.IsDone){Next(t);if(!selfTest)ShowSuccess();}if(!selfTest)SaveTasks();UpdateSummary();window.Dispatcher.BeginInvoke(new Action(delegate{view.Refresh();RefreshDay();UpdateSummary();}),DispatcherPriority.ContextIdle);}}
 void Next(TaskItem t){if(t.RecurrenceCreated||t.Repeat=="不重复")return;DateTime d;if(!DateTime.TryParseExact(t.DueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d))return;DateTime n;if(t.Repeat=="每天")n=d.AddDays(1);else if(t.Repeat=="每周")n=d.AddDays(7);else if(t.Repeat=="每月")n=d.AddMonths(1);else if(t.Repeat=="每年")n=d.AddYears(1);else return;loading=true;t.RecurrenceCreated=true;var c=new TaskItem{Title=t.Title,DueDate=n.ToString("yyyy-MM-dd"),DueTime=t.DueTime,Category=t.Category,Repeat=t.Repeat,Reminder=t.Reminder,Location=t.Location,Notes=t.Notes};c.PropertyChanged+=Changed;tasks.Add(c);loading=false;}
  void RunHoverTest(object s,EventArgs e){
   window.ContentRendered-=RunHoverTest;
   var first=new TaskItem{Title="Hover test first"};var second=new TaskItem{Title="Hover test second"};tasks.Add(first);tasks.Add(second);view.Refresh();taskList.UpdateLayout();
   var row=taskList.ItemContainerGenerator.ContainerFromItem(first) as ListBoxItem;
   var next=taskList.ItemContainerGenerator.ContainerFromItem(second) as ListBoxItem;
   if(row==null||next==null){Environment.ExitCode=21;window.Close();return;}
   double before=next.PointToScreen(new Point(0,0)).Y;
   row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.PreviewMouseMoveEvent});
   todayTab.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseEnterEvent});
   RunLater(420,delegate{
    double after=next.PointToScreen(new Point(0,0)).Y;
    bool stable=Math.Abs(after-before)<.5&&Math.Abs(Motion.Translate(todayTab).Y)<.01&&Math.Abs(Motion.Scale(todayTab).ScaleX-1)<.01;
     ShowEditor(null);editTitle.Text="Time wheel test";SetTimeValue(false,10,false);SetTimeValue(true,2,false);
     ScrollTimeWheel(editHour,new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});
     RunLater(280,delegate{
      SaveEditor();bool time=tasks.Any(x=>x.Title=="Time wheel test"&&x.DueTime=="10:30");
      Environment.ExitCode=!stable?22:time?0:23;window.Close();
     });
   });
  }
  void RunSelfTest(object s,EventArgs e){
  window.ContentRendered-=RunSelfTest;
  SetMode("Calendar");monthCalendar.ApplyTemplate();monthCalendar.UpdateLayout();
  var calendarItem=monthCalendar.Template.FindName("PART_CalendarItem",monthCalendar) as System.Windows.Controls.Primitives.CalendarItem;
  if(calendarItem!=null)calendarItem.ApplyTemplate();
  Grid monthView=calendarItem==null?null:calendarItem.Template.FindName("PART_MonthView",calendarItem) as Grid;
  Grid yearView=calendarItem==null?null:calendarItem.Template.FindName("PART_YearView",calendarItem) as Grid;
  Button headerButton=calendarItem==null?null:calendarItem.Template.FindName("PART_HeaderButton",calendarItem) as Button;
  DateTime original=monthCalendar.SelectedDate??DateTime.Today;
  DateTime next=original.AddMonths(1);
  monthCalendar.DisplayDate=next;SyncDisplayedMonth();
  bool calendar=monthCalendar.SelectedDate.HasValue&&monthCalendar.SelectedDate.Value.Year==next.Year&&monthCalendar.SelectedDate.Value.Month==next.Month&&selectedDayHeading.Text.Contains(next.ToString("yyyy年M月"));
  int calendarFailure=calendar?0:4;
  if(headerButton!=null){if(!TestCalendarPress(headerButton)&&calendarFailure==0)calendarFailure=5;}
  else if(calendarFailure==0)calendarFailure=6;
  monthCalendar.UpdateLayout();UpdateCalendarNavigation();
  calendar=monthCalendar.DisplayMode==CalendarMode.Year&&dayList.Visibility==Visibility.Collapsed&&calendarNavHint.Visibility==Visibility.Visible&&monthView!=null&&monthView.Visibility!=Visibility.Visible&&yearView!=null&&yearView.Visibility==Visibility.Visible&&yearView.Children.Count>=12;
  if(!calendar&&calendarFailure==0)calendarFailure=7;
  if(headerButton!=null){if(!TestCalendarPress(headerButton)&&calendarFailure==0)calendarFailure=8;}
  monthCalendar.UpdateLayout();
  calendar=monthCalendar.DisplayMode==CalendarMode.Decade&&monthView.Visibility!=Visibility.Visible&&yearView.Visibility==Visibility.Visible&&yearView.Children.Count>=12;
  if(!calendar&&calendarFailure==0)calendarFailure=9;
  var yearCell=yearView.Children.OfType<System.Windows.Controls.Primitives.CalendarButton>().FirstOrDefault(x=>x.DataContext is DateTime);
  if(yearCell!=null){DateTime chosen=(DateTime)yearCell.DataContext;if((!TestCalendarPress(yearCell)||monthCalendar.DisplayMode!=CalendarMode.Year||monthCalendar.DisplayDate.Year!=chosen.Year)&&calendarFailure==0)calendarFailure=10;}
  else if(calendarFailure==0)calendarFailure=11;
  var monthCell=yearView.Children.OfType<System.Windows.Controls.Primitives.CalendarButton>().FirstOrDefault(x=>x.DataContext is DateTime);
  if(monthCell!=null){DateTime chosen=(DateTime)monthCell.DataContext;if((!TestCalendarPress(monthCell)||monthCalendar.DisplayMode!=CalendarMode.Month||monthCalendar.SelectedDate.Value.Month!=chosen.Month)&&calendarFailure==0)calendarFailure=12;}
  else if(calendarFailure==0)calendarFailure=13;
  monthCalendar.DisplayMode=CalendarMode.Month;monthCalendar.DisplayDate=original;SyncDisplayedMonth();
  calendar=monthView.Visibility==Visibility.Visible&&yearView.Visibility!=Visibility.Visible;
  if(!calendar&&calendarFailure==0)calendarFailure=14;
  SetMode("Agenda");
  SetMode("Calendar");SetMode("Today");SetMode("Agenda");
  var p=new TaskItem{Title="__TEST__"};p.PropertyChanged+=Changed;tasks.Add(p);view.Refresh();ShowDetails(p);
  bool detail=detailsPanel.Visibility==Visibility.Visible&&detailTitle.Text==p.Title;HideOverlays();
   ShowEditor(null);ShowTitleError();ClearTitleError();editCategory.ApplyTemplate();editCategory.IsDropDownOpen=true;Popup testPopup=editCategory.Template.FindName("PART_Popup",editCategory) as Popup;Border testBorder=testPopup==null?null:testPopup.Child as Border;ScrollViewer testContent=testBorder==null?null:testBorder.Child as ScrollViewer;if(testContent!=null)CloseDropdown(editCategory,testContent,false);else editCategory.IsDropDownOpen=false;HideOverlays();ShowSuccess();SwapIcon(pinButton,Convert.ToString(pinButton.Content));
  window.Dispatcher.BeginInvoke(new Action(delegate{p.IsDone=true;window.Dispatcher.BeginInvoke(new Action(delegate{
   bool a=!view.Cast<object>().Contains(p);mode="Completed";view.Refresh();bool b=view.Cast<object>().Contains(p);
   p.PropertyChanged-=Changed;tasks.Remove(p);RunLater(2100,delegate{bool motion=editorPanel.Visibility==Visibility.Collapsed&&editorError.Visibility==Visibility.Collapsed&&successToast.Visibility==Visibility.Collapsed&&!editCategory.IsDropDownOpen;Environment.ExitCode=a&&b&&detail&&motion&&calendarFailure==0?0:calendarFailure==0?3:calendarFailure;window.Close();});
  }),DispatcherPriority.ApplicationIdle);}),DispatcherPriority.Background);
 }
 bool TestCalendarPress(Button button){
  var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left);down.RoutedEvent=Mouse.PreviewMouseDownEvent;button.RaiseEvent(down);
  var up=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left);up.RoutedEvent=Mouse.PreviewMouseUpEvent;button.RaiseEvent(up);
  return down.Handled&&up.Handled;
 }
 void Wire(){
  Find<Grid>("DragBar").MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(e.ClickCount==2)window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else window.DragMove();};
  Find<Button>("CloseButton").Click+=delegate{window.Close();};Find<Button>("MinButton").Click+=delegate{window.WindowState=WindowState.Minimized;};pinButton.Click+=delegate{window.Topmost=!window.Topmost;SwapIcon(pinButton,window.Topmost?"◆":"◇");SaveSettings();};
  Find<Button>("NewButton").Click+=delegate{HideOverlays();ShowEditor(null);};Find<Button>("CancelEditor").Click+=delegate{CloseOverlay(editorPanel);};Find<Button>("SaveEditor").Click+=delegate{SaveEditor();};
  Find<Button>("SettingsButton").Click+=delegate{HideOverlays();OpenOverlay(settingsPanel,true);};Find<Button>("CloseSettings").Click+=delegate{CloseOverlay(settingsPanel);SaveSettings();};
  accentColorButton.Click+=delegate{int version=++accentPickerVersion;accentPickerOpen=!accentPickerOpen;if(accentPickerOpen){accentPickerPanel.Visibility=Visibility.Visible;accentPickerPanel.IsHitTestVisible=true;accentHexInput.Text=AccentHex();Motion.Enter(accentPickerPanel,Motion.Fast,0,4,.97,0);}else{accentPickerPanel.IsHitTestVisible=false;Motion.Exit(accentPickerPanel,delegate{if(version==accentPickerVersion){accentPickerPanel.Visibility=Visibility.Collapsed;accentPickerPanel.IsHitTestVisible=true;}},Motion.Quick,0,-4,.99,0);}};
  Find<Button>("ApplyAccentButton").Click+=delegate{ApplyAccentInput();};
  accentHexInput.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){ApplyAccentInput();e.Handled=true;}};
  accentHexInput.TextChanged+=delegate{accentInputHint.Text="输入 #RRGGBB，可使用任意主题色";accentInputHint.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondaryBrush");};
  Find<Button>("CloseDetails").Click+=delegate{CloseOverlay(detailsPanel);};Find<Button>("DetailEdit").Click+=delegate{TaskItem t=detailsTask;HideOverlays();if(t!=null)ShowEditor(t);};
  themeSelect.SelectionChanged+=delegate{ApplyAppearance();QueueSettings();};opacitySlider.ValueChanged+=delegate{ApplyAppearance();QueueSettings();};
  opacitySlider.Loaded+=delegate{AttachOpacityThumb();};window.ContentRendered+=delegate{MoveTabPill(false);};
  searchBox.TextChanged+=delegate{searchHint.Visibility=searchBox.Text.Length==0?Visibility.Visible:Visibility.Collapsed;view.Refresh();UpdateSummary();};agendaTab.Click+=delegate{SetMode("Agenda");};todayTab.Click+=delegate{SetMode("Today");};calendarTab.Click+=delegate{SetMode("Calendar");};completedTab.Click+=delegate{SetMode("Completed");};monthCalendar.SelectedDatesChanged+=delegate{RefreshDay();};
  monthCalendar.PreviewMouseDown+=CalendarHeaderMouseDown;monthCalendar.PreviewMouseUp+=CalendarMouseUp;monthCalendar.PreviewKeyDown+=CalendarHeaderKeyDown;
  monthCalendar.DisplayDateChanged+=delegate{QueueCalendarSync();};monthCalendar.DisplayModeChanged+=delegate{UpdateCalendarNavigation();QueueCalendarSync();};
  editDate.CalendarOpened+=delegate{window.Dispatcher.BeginInvoke(new Action(HookDatePickerCalendar),DispatcherPriority.Loaded);};
   taskList.AddHandler(Button.ClickEvent,new RoutedEventHandler(TaskButton));taskList.PreviewMouseLeftButtonUp+=TaskCardClick;dayList.PreviewMouseLeftButtonUp+=DayCardClick;
   editTitle.TextChanged+=delegate{ClearTitleError();};foreach(ComboBox combo in new[]{editCategory,editRepeat,editReminder,themeSelect}){ComboBox local=combo;local.DropDownOpened+=delegate{AnimateDropdown(local);};}
   window.SourceInitialized+=delegate{HwndSource source=(HwndSource)PresentationSource.FromVisual(window);source.AddHook(WindowProc);ApplyAppearance();};settingsTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};settingsTimer.Tick+=delegate{settingsTimer.Stop();SaveSettings();};window.LocationChanged+=ScheduleSettings;window.SizeChanged+=ScheduleSettings;
  window.Closing+=delegate{if(settingsTimer!=null)settingsTimer.Stop();if(reminderTimer!=null)reminderTimer.Stop();if(notifyIcon!=null){notifyIcon.Visible=false;notifyIcon.Dispose();}if(!selfTest){SaveTasks();SaveSettings();}};
 }
 void TaskButton(object sender,RoutedEventArgs e){DependencyObject d=e.OriginalSource as DependencyObject;while(d!=null&&!(d is Button))d=VisualTreeHelper.GetParent(d);Button b=d as Button;TaskItem t=b==null?null:b.Tag as TaskItem;if(t==null)return;if(b.Name=="EditTask")ShowEditor(t);else if(b.Name=="DeleteTask"){t.PropertyChanged-=Changed;tasks.Remove(t);SaveTasks();view.Refresh();RefreshDay();UpdateSummary();}e.Handled=true;}
 void HookDatePickerCalendar(){
  editDate.ApplyTemplate();Popup popup=editDate.Template.FindName("PART_Popup",editDate) as Popup;
  if(popup==null)return;
  Border existing=popup.Child as Border;
  if(existing!=null&&existing.Tag as string=="StyledDatePopup"){Motion.Enter(existing,250,0,6,.97,0);return;}
  var calendar=popup.Child as System.Windows.Controls.Calendar;
  if(calendar==null)return;
  popup.Child=null;
  calendar.Style=(Style)window.FindResource("ModernCalendar");
  var header=new Grid{Margin=new Thickness(7,3,7,6)};
  var heading=new TextBlock{Text="选择日期",FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};heading.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimaryBrush");header.Children.Add(heading);
  var label=new Border{CornerRadius=new CornerRadius(9),Padding=new Thickness(9,4,9,4),HorizontalAlignment=HorizontalAlignment.Right};label.SetResourceReference(Border.BackgroundProperty,"AccentTintBrush");
  var labelText=new TextBlock{Text="DATE",FontSize=10,FontWeight=FontWeights.SemiBold};labelText.SetResourceReference(TextBlock.ForegroundProperty,"AccentTextBrush");label.Child=labelText;header.Children.Add(label);
  dateTodayButton=new Button{Content="回到今天",Style=(Style)window.FindResource("IconButton"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,3,4,0)};dateTodayButton.SetResourceReference(Button.ForegroundProperty,"AccentTextBrush");dateTodayButton.Click+=delegate{editDate.SelectedDate=DateTime.Today;editDate.IsDropDownOpen=false;};
  var stack=new StackPanel();stack.Children.Add(header);stack.Children.Add(calendar);stack.Children.Add(dateTodayButton);
  var shell=new Border{Tag="StyledDatePopup",Margin=new Thickness(0,7,0,10),CornerRadius=new CornerRadius(17),BorderThickness=new Thickness(1),Padding=new Thickness(8),Effect=(Effect)window.FindResource("PopupShadow"),Child=stack};
  shell.SetResourceReference(Border.BackgroundProperty,"PopupSurfaceBrush");shell.SetResourceReference(Border.BorderBrushProperty,"GlassEdgeBrush");popup.Child=shell;
  Motion.Enter(shell,250,0,6,.97,0);
 }
 void HideOverlays(){foreach(Border panel in new[]{editorPanel,detailsPanel,settingsPanel}){overlayVersions[panel]=OverlayVersion(panel)+1;panel.Visibility=Visibility.Collapsed;panel.IsHitTestVisible=true;}scrimVersion++;overlayScrim.BeginAnimation(UIElement.OpacityProperty,null);overlayScrim.Visibility=Visibility.Collapsed;overlayScrim.Opacity=1;}
 static T Parent<T>(DependencyObject d)where T:DependencyObject{while(d!=null){T found=d as T;if(found!=null)return found;d=VisualTreeHelper.GetParent(d);}return null;}
 void CalendarHeaderMouseDown(object sender,MouseButtonEventArgs e){
  if(e.ChangedButton!=MouseButton.Left)return;
  suppressCalendarMouseUp=false;
  Button button=Parent<Button>(e.OriginalSource as DependencyObject);
  if(button!=null&&button.Name=="PART_HeaderButton"){e.Handled=true;suppressCalendarMouseUp=true;AdvanceCalendarMode();return;}
  var cell=Parent<System.Windows.Controls.Primitives.CalendarButton>(e.OriginalSource as DependencyObject);
  if(cell!=null&&cell.DataContext is DateTime){e.Handled=true;suppressCalendarMouseUp=true;ChooseCalendarCell((DateTime)cell.DataContext);}
 }
 void CalendarMouseUp(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left&&suppressCalendarMouseUp){suppressCalendarMouseUp=false;e.Handled=true;}}
 void CalendarHeaderKeyDown(object sender,KeyEventArgs e){
  if(e.Key!=Key.Enter&&e.Key!=Key.Space)return;
  Button button=Parent<Button>(Keyboard.FocusedElement as DependencyObject);
  if(button!=null&&button.Name=="PART_HeaderButton"){e.Handled=true;AdvanceCalendarMode();return;}
  var cell=Parent<System.Windows.Controls.Primitives.CalendarButton>(Keyboard.FocusedElement as DependencyObject);
  if(cell!=null&&cell.DataContext is DateTime){e.Handled=true;ChooseCalendarCell((DateTime)cell.DataContext);}
 }
 void AdvanceCalendarMode(){
  if(monthCalendar.DisplayMode==CalendarMode.Month)monthCalendar.DisplayMode=CalendarMode.Year;
  else if(monthCalendar.DisplayMode==CalendarMode.Year)monthCalendar.DisplayMode=CalendarMode.Decade;
 }
 void ChooseCalendarCell(DateTime choice){
  DateTime displayed=monthCalendar.DisplayDate;
  if(monthCalendar.DisplayMode==CalendarMode.Year){
   int day=Math.Min(displayed.Day,DateTime.DaysInMonth(choice.Year,choice.Month));
   monthCalendar.DisplayDate=new DateTime(choice.Year,choice.Month,day);
   monthCalendar.DisplayMode=CalendarMode.Month;
   SyncDisplayedMonth();
  }else if(monthCalendar.DisplayMode==CalendarMode.Decade){
   int day=Math.Min(displayed.Day,DateTime.DaysInMonth(choice.Year,displayed.Month));
   monthCalendar.DisplayDate=new DateTime(choice.Year,displayed.Month,day);
   monthCalendar.DisplayMode=CalendarMode.Year;
  }
 }
 void TaskCardClick(object sender,MouseButtonEventArgs e){DependencyObject d=e.OriginalSource as DependencyObject;if(Parent<CheckBox>(d)!=null||Parent<Button>(d)!=null)return;ListBoxItem row=Parent<ListBoxItem>(d);TaskItem t=row==null?null:row.DataContext as TaskItem;if(t!=null)ShowDetails(t);}
 void DayCardClick(object sender,MouseButtonEventArgs e){ListBoxItem row=Parent<ListBoxItem>(e.OriginalSource as DependencyObject);DayEntry item=row==null?null:row.DataContext as DayEntry;if(item!=null&&item.Task!=null)ShowDetails(item.Task);}
 void ShowDetails(TaskItem t){detailsTask=t;HideOverlays();detailTitle.Text=t.Title;detailStatus.Text=t.IsDone?"✓ 已完成":"○ 待完成";DateTime d;string date=DateTime.TryParseExact(t.DueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d)?d.ToString("yyyy年M月d日 dddd",CultureInfo.GetCultureInfo("zh-CN")):t.DueDate;detailDateTime.Text=date+(String.IsNullOrEmpty(t.DueTime)?" · 全天":" · "+t.DueTime);detailCategoryRepeat.Text=t.Category+" · "+t.Repeat;detailReminder.Text=ReminderText(t.Reminder);detailLocation.Text=String.IsNullOrWhiteSpace(t.Location)?"未设置":t.Location;detailNotes.Text=String.IsNullOrWhiteSpace(t.Notes)?"无备注":t.Notes;OpenOverlay(detailsPanel,false);}
 static string ReminderText(int v){return v<0?"不提醒":v==0?"准时提醒":v==10?"提前 10 分钟":v==30?"提前 30 分钟":v==60?"提前 1 小时":v==1440?"提前 1 天":"提前 "+v+" 分钟";}
 void QueueSettings(){if(selfTest||settingsTimer==null)return;settingsTimer.Stop();settingsTimer.Start();}
 void ApplyAppearance(){
  if(themeSelect==null||opacitySlider==null||themeSelect.SelectedIndex<0)return;
  bool dark=themeSelect.SelectedIndex==1;byte glass=(byte)Math.Round(190+(opacitySlider.Value-55)*1.22);
  UpdateOpacityReadout();
  window.Background=Brushes.Transparent;
  Color glassBase=dark?Color.FromRgb(24,35,53):Color.FromRgb(246,250,255);
  Color upper=Mix(glassBase,accentColor,dark?.13:.07),lower=Mix(glassBase,accentColor,dark?.035:.02);
  window.Resources["SurfaceBrush"]=new LinearGradientBrush(new GradientStopCollection{
   new GradientStop(Color.FromArgb(glass,upper.R,upper.G,upper.B),0),
   new GradientStop(Color.FromArgb((byte)Math.Min(250,glass+3),glassBase.R,glassBase.G,glassBase.B),.54),
   new GradientStop(Color.FromArgb((byte)Math.Min(252,glass+7),lower.R,lower.G,lower.B),1)
  },new Point(0,0),new Point(1,1));
  Color secondaryGlow=Mix(accentColor,dark?Color.FromRgb(150,169,226):Color.FromRgb(174,203,234),.48);
  window.Resources["GlassAmbientBrush"]=AmbientBrush(accentColor,(byte)(dark?40:46),new Point(.85,.04));
  window.Resources["GlassAmbientSecondaryBrush"]=AmbientBrush(secondaryGlow,(byte)(dark?25:28),new Point(.08,.92));
  SetBrush("PanelBrush",dark,Color.FromArgb(250,27,38,57),Color.FromArgb(250,249,251,255));
  SetBrush("PopupSurfaceBrush",dark,Color.FromRgb(33,45,65),Color.FromRgb(251,253,255));
  SetBrush("InputBrush",dark,Color.FromArgb((byte)Math.Min(246,glass+7),43,57,78),Color.FromArgb((byte)Math.Min(246,glass+1),255,255,255));
  SetBrush("ItemBrush",dark,Color.FromArgb((byte)Math.Min(245,glass+4),38,52,74),Color.FromArgb((byte)Math.Min(240,glass+2),255,255,255));
  SetBrush("ContentWellBrush",dark,Color.FromArgb(36,177,211,240),Color.FromArgb(78,255,255,255));
  SetBrush("TabRailBrush",dark,Color.FromArgb(82,31,49,74),Color.FromArgb(155,255,255,255));
  SetBrush("OverlayScrimBrush",dark,Color.FromArgb(83,4,12,24),Color.FromArgb(72,42,67,93));
  SetBrush("GlassEdgeBrush",dark,Color.FromArgb(110,192,217,241),Color.FromArgb(220,255,255,255));
  SetBrush("TextPrimaryBrush",dark,Color.FromRgb(246,250,255),Color.FromRgb(31,47,67));
  SetBrush("TextSecondaryBrush",dark,Color.FromRgb(207,221,237),Color.FromRgb(75,95,120));
  SetBrush("TextMutedBrush",dark,Color.FromRgb(188,205,224),Color.FromRgb(96,114,137));
  SetBrush("ErrorBrush",dark,Color.FromRgb(255,150,159),Color.FromRgb(198,61,75));
  SetBrush("BorderBrush",dark,Color.FromRgb(103,128,157),Color.FromRgb(195,210,228));
  Color surface=dark?Color.FromRgb(27,38,57):Color.FromRgb(249,251,255);
  Color hoverSurface=Mix(surface,accentColor,dark?.20:.10),selectedSurface=Mix(surface,accentColor,dark?.31:.19);
  window.Resources["HoverBrush"]=new SolidColorBrush(Color.FromArgb(245,hoverSurface.R,hoverSurface.G,hoverSurface.B));
  window.Resources["SelectedBrush"]=new SolidColorBrush(Color.FromArgb(250,selectedSurface.R,selectedSurface.G,selectedSurface.B));
  Color accentText=AccessibleAccent(accentColor,surface);
  Color onAccent=Contrast(accentColor,Colors.White)>=Contrast(accentColor,Colors.Black)?Colors.White:Colors.Black;
  Color hover=Mix(accentColor,onAccent==Colors.White?Colors.Black:Colors.White,.08);
  window.Resources["AccentBrush"]=new SolidColorBrush(accentColor);
  window.Resources["AccentHoverBrush"]=new SolidColorBrush(hover);
  Color gradientStart=EnsureOnAccentContrast(Mix(accentColor,Colors.White,.08),onAccent),gradientEnd=EnsureOnAccentContrast(Mix(accentColor,Colors.Black,.06),onAccent);
  window.Resources["AccentGradientBrush"]=new LinearGradientBrush(gradientStart,gradientEnd,new Point(0,0),new Point(1,1));
  window.Resources["AccentHoverGradientBrush"]=new LinearGradientBrush(EnsureOnAccentContrast(Mix(gradientStart,Colors.White,.09),onAccent),EnsureOnAccentContrast(accentColor,onAccent),new Point(0,0),new Point(1,1));
  window.Resources["AccentTextBrush"]=new SolidColorBrush(accentText);
  window.Resources["AccentTintBrush"]=new SolidColorBrush(Color.FromArgb(dark?(byte)70:(byte)35,accentColor.R,accentColor.G,accentColor.B));
  window.Resources["OnAccentBrush"]=new SolidColorBrush(onAccent);
  window.Resources["AccentBadgeBrush"]=new SolidColorBrush(accentColor);
  if(((SolidColorBrush)TaskItem.PersonalCategoryBrush).Color!=accentColor){TaskItem.PersonalCategoryBrush=new SolidColorBrush(accentColor);if(tasks!=null)foreach(TaskItem task in tasks)task.RefreshCategoryBrush();}
  if(accentColorLabel!=null)accentColorLabel.Text=AccentHex()+"  ·  自由选择颜色  ▾";
  if(accentPalette!=null)foreach(Button swatch in accentPalette.Children.OfType<Button>()){Color color=(Color)swatch.Tag;swatch.BorderBrush=new SolidColorBrush(color==accentColor?accentColor:(dark?Color.FromRgb(90,102,122):Color.FromRgb(207,215,226)));swatch.BorderThickness=new Thickness(color==accentColor?2.5:1.4);}
  UpdateTabs();
 }
 static Brush AmbientBrush(Color hue,byte alpha,Point center){var brush=new RadialGradientBrush{Center=center,GradientOrigin=center,RadiusX=.72,RadiusY=.75,GradientStops=new GradientStopCollection{new GradientStop(Color.FromArgb(alpha,hue.R,hue.G,hue.B),0),new GradientStop(Color.FromArgb(0,hue.R,hue.G,hue.B),1)}};brush.Freeze();return brush;}
 static Color Mix(Color a,Color b,double fraction){return Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*fraction),(byte)Math.Round(a.G+(b.G-a.G)*fraction),(byte)Math.Round(a.B+(b.B-a.B)*fraction));}
 static double Channel(byte value){double x=value/255.0;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}
 static double Luminance(Color color){return .2126*Channel(color.R)+.7152*Channel(color.G)+.0722*Channel(color.B);}
 static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
 static Color AccessibleAccent(Color accent,Color background){Color target=Luminance(background)>.5?Colors.Black:Colors.White;for(double f=0;f<=1;f+=.02){Color candidate=Mix(accent,target,f);if(Contrast(candidate,background)>=4.5)return candidate;}return target;}
 static Color EnsureOnAccentContrast(Color background,Color foreground){Color target=foreground==Colors.White?Colors.Black:Colors.White;for(double f=0;f<=1;f+=.02){Color candidate=Mix(background,target,f);if(Contrast(candidate,foreground)>=4.5)return candidate;}return target;}
 bool TextContrastValid(){var panel=(SolidColorBrush)window.Resources["PanelBrush"];foreach(string key in new[]{"TextPrimaryBrush","TextSecondaryBrush","TextMutedBrush","AccentTextBrush","ErrorBrush"})if(Contrast(((SolidColorBrush)window.Resources[key]).Color,panel.Color)<4.5)return false;Color onAccent=((SolidColorBrush)window.Resources["OnAccentBrush"]).Color;if(Contrast(onAccent,accentColor)<4.5)return false;foreach(string key in new[]{"AccentGradientBrush","AccentHoverGradientBrush"})foreach(GradientStop stop in ((LinearGradientBrush)window.Resources[key]).GradientStops)if(Contrast(onAccent,stop.Color)<4.5)return false;return true;}
 void SetBrush(string key,bool dark,Color darkColor,Color lightColor){window.Resources[key]=new SolidColorBrush(dark?darkColor:lightColor);}
 void SetMode(string v){string old=mode;mode=v;Grid next=v=="Calendar"?calendarPanel:agendaPanel,previous=old=="Calendar"?calendarPanel:agendaPanel;int version=++pageVersion;next.Visibility=Visibility.Visible;Panel.SetZIndex(next,1);Panel.SetZIndex(previous,0);view.Refresh();RefreshDay();UpdateTabs();UpdateSummary();SwapText(headerTitle,v=="Calendar"?"月历":v=="Today"?"今天":v=="Completed"?"已完成":"我的日程");if(old==v){previous.Visibility=Visibility.Visible;return;}if(previous!=next){Motion.Exit(previous,delegate{if(version==pageVersion)previous.Visibility=Visibility.Collapsed;},150,-8,0,1,0);Motion.Enter(next,250,8,0,1,0);}else calendarPanel.Visibility=Visibility.Collapsed;}
 void UpdateTabs(){SetTab(agendaTab,mode=="Agenda");SetTab(todayTab,mode=="Today");SetTab(calendarTab,mode=="Calendar");SetTab(completedTab,mode=="Completed");if(tabRailInner!=null)MoveTabPill(pillMode!=""&&pillMode!=mode);}static void SetTab(Button b,bool on){b.Background=Brushes.Transparent;b.SetResourceReference(Button.ForegroundProperty,on?"AccentTextBrush":"TextSecondaryBrush");}
 void ShowEditor(TaskItem t){editingTask=t;Find<TextBlock>("EditorHeading").Text=t==null?"新建日程":"编辑日程";editTitle.Text=t==null?"":t.Title;ClearTitleError();DateTime d;editDate.SelectedDate=t!=null&&DateTime.TryParseExact(t.DueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out d)?d:(mode=="Calendar"&&monthCalendar.SelectedDate.HasValue?monthCalendar.SelectedDate.Value:DateTime.Today);SelectTime(t==null?"":t.DueTime);Select(editCategory,t==null?"个人":t.Category);Select(editRepeat,t==null?"不重复":t.Repeat);SelectReminder(t==null?-1:t.Reminder);editLocation.Text=t==null?"":t.Location;editNotes.Text=t==null?"":t.Notes;OpenOverlay(editorPanel,false);editTitle.Focus();}
 void SelectTime(string value){if(String.IsNullOrEmpty(value)){SetTimeValue(false,0,false);SetTimeValue(true,0,false);return;}string[] p=value.Split(':');int h,m;if(p.Length!=2||!Int32.TryParse(p[0],out h)||!Int32.TryParse(p[1],out m)){SetTimeValue(false,0,false);SetTimeValue(true,0,false);return;}SetTimeValue(false,Math.Max(1,Math.Min(24,h+1)),false);SetTimeValue(true,m>=45?3:m>=30?2:m>=15?1:0,false);}static void Select(ComboBox b,string v){for(int i=0;i<b.Items.Count;i++){ComboBoxItem x=(ComboBoxItem)b.Items[i];if(Convert.ToString(x.Content)==v){b.SelectedIndex=i;return;}}b.SelectedIndex=0;}void SelectReminder(int v){for(int i=0;i<editReminder.Items.Count;i++){ComboBoxItem x=(ComboBoxItem)editReminder.Items[i];if(Convert.ToString(x.Tag)==v.ToString()){editReminder.SelectedIndex=i;return;}}editReminder.SelectedIndex=0;}static string ComboText(ComboBox b){return Convert.ToString(((ComboBoxItem)b.SelectedItem).Content);}
 void SaveEditor(){string title=editTitle.Text.Trim();if(title.Length==0){ShowTitleError();return;}editing=true;TaskItem t=editingTask;if(t==null){t=new TaskItem();t.PropertyChanged+=Changed;tasks.Add(t);}t.Title=title;t.DueDate=(editDate.SelectedDate??DateTime.Today).ToString("yyyy-MM-dd");t.DueTime=hourIndex<=0?"":(hourIndex-1).ToString("00")+":"+(minuteIndex*15).ToString("00");t.Category=ComboText(editCategory);t.Repeat=ComboText(editRepeat);t.Reminder=Int32.Parse(Convert.ToString(((ComboBoxItem)editReminder.SelectedItem).Tag));t.Location=editLocation.Text.Trim();t.Notes=editNotes.Text.Trim();t.Notified="";t.RecurrenceCreated=false;editing=false;CloseOverlay(editorPanel);SaveTasks();view.Refresh();RefreshDay();UpdateSummary();ShowSuccess();}
 void QueueCalendarSync(){window.Dispatcher.BeginInvoke(new Action(SyncDisplayedMonth),DispatcherPriority.Loaded);}
 void SyncDisplayedMonth(){
  if(monthCalendar.DisplayMode==CalendarMode.Month){
   DateTime displayed=monthCalendar.DisplayDate;
   DateTime selected=monthCalendar.SelectedDate??DateTime.Today;
   if(displayed.Year!=selected.Year||displayed.Month!=selected.Month){
    int day=Math.Min(selected.Day,DateTime.DaysInMonth(displayed.Year,displayed.Month));
    monthCalendar.SelectedDate=new DateTime(displayed.Year,displayed.Month,day);
   }
  }
  RefreshDay();
 }
 void UpdateCalendarNavigation(){
  bool datesVisible=monthCalendar.DisplayMode==CalendarMode.Month;
  dayList.Visibility=datesVisible?Visibility.Visible:Visibility.Collapsed;
  selectedDayHeading.Visibility=datesVisible?Visibility.Visible:Visibility.Collapsed;
  calendarNavHint.Visibility=datesVisible?Visibility.Collapsed:Visibility.Visible;
  if(datesVisible)selectedDayHeading.Text=(monthCalendar.SelectedDate??DateTime.Today).ToString("yyyy年M月d日 dddd",CultureInfo.GetCultureInfo("zh-CN"))+" 的日程";
  else calendarNavHint.Text=monthCalendar.DisplayMode==CalendarMode.Year?"请选择月份，随后可点选具体日期。":"请选择年份，再选择月份和日期。";
 }
 void RefreshDay(){if(dayList==null)return;UpdateCalendarNavigation();var list=new ObservableCollection<DayEntry>();string day=(monthCalendar.SelectedDate??DateTime.Today).ToString("yyyy-MM-dd");foreach(TaskItem t in tasks.Where(x=>x.DueDate==day).OrderBy(x=>x.DueTime)){string p=t.IsDone?"✓ ":String.IsNullOrEmpty(t.DueTime)?"全天  ":t.DueTime+"  ";list.Add(new DayEntry{Display=p+t.Title+"  ·  "+t.Category,Task=t});}if(list.Count==0)list.Add(new DayEntry{Display="这一天没有日程"});dayList.ItemsSource=list;if(mode=="Calendar"&&monthCalendar.DisplayMode==CalendarMode.Month&&lastRevealedDay!=day){lastRevealedDay=day;Motion.Enter(dayList,Motion.Fast,0,8,1,0);}}
 void UpdateSummary(){int n=tasks.Count(x=>!x.IsDone),total=tasks.Count,today=tasks.Count(x=>!x.IsDone&&x.DueDate==DateTime.Today.ToString("yyyy-MM-dd"));if(n!=lastPending){UpdateDigits(pendingDigits,n);lastPending=n;}if(total!=lastTotal){UpdateDigits(totalDigits,total);lastTotal=total;}UpdateBadge(today);if(mode!="Calendar"){emptyState.Visibility=view.IsEmpty?Visibility.Visible:Visibility.Collapsed;SwapText(emptyTitle,searchBox.Text.Length>0?"没有匹配的日程":mode=="Completed"?"没有已完成日程":"没有日程");}}
 int OverlayVersion(UIElement element){int value;return overlayVersions.TryGetValue(element,out value)?value:0;}
 void ShowOverlayScrim(){scrimVersion++;if(overlayScrim.Visibility!=Visibility.Visible){overlayScrim.Visibility=Visibility.Visible;Motion.Tween(overlayScrim,UIElement.OpacityProperty,0,1,Motion.Quick);}else Motion.Tween(overlayScrim,UIElement.OpacityProperty,overlayScrim.Opacity,1,Motion.Quick);}
 void HideOverlayScrimIfClear(){if(new[]{editorPanel,detailsPanel,settingsPanel}.Any(x=>x.Visibility==Visibility.Visible))return;int version=++scrimVersion;Motion.Tween(overlayScrim,UIElement.OpacityProperty,overlayScrim.Opacity,0,Motion.Quick,false,delegate{if(version==scrimVersion)overlayScrim.Visibility=Visibility.Collapsed;});}
 void OpenOverlay(Border panel,bool reveal){overlayVersions[panel]=OverlayVersion(panel)+1;panel.Visibility=Visibility.Visible;panel.IsHitTestVisible=true;ShowOverlayScrim();if(reveal)Motion.Enter(panel,Motion.Fast,0,8,1,0);else Motion.Enter(panel,Motion.Fast,0,0,.96,0);}
 void CloseOverlay(Border panel){if(panel.Visibility!=Visibility.Visible)return;int version=OverlayVersion(panel)+1;overlayVersions[panel]=version;panel.IsHitTestVisible=false;Motion.Exit(panel,delegate{if(OverlayVersion(panel)==version){panel.Visibility=Visibility.Collapsed;panel.IsHitTestVisible=true;HideOverlayScrimIfClear();}},Motion.Quick,0,panel==settingsPanel?8:0,panel==settingsPanel?1:.96,0);}
 void SwapText(TextBlock block,string value){if(block.Text==value)return;int version;textVersions.TryGetValue(block,out version);version++;textVersions[block]=version;if(!Motion.Enabled){block.Text=value;block.Opacity=1;Motion.Translate(block).Y=0;return;}bool title=block==headerTitle;int exit=title?75:80,enter=title?150:80;if(title)Motion.Tween(Motion.Translate(block),TranslateTransform.YProperty,Motion.Translate(block).Y,-4,exit);Motion.Tween(block,UIElement.OpacityProperty,block.Opacity,0,exit,false,delegate{int current;if(!textVersions.TryGetValue(block,out current)||current!=version)return;block.Text=value;if(title){Motion.Translate(block).Y=4;Motion.Tween(Motion.Translate(block),TranslateTransform.YProperty,4,0,enter);}Motion.Tween(block,UIElement.OpacityProperty,0,1,enter);});}
 void UpdateDigits(StackPanel host,int value){host.Children.Clear();string digits=value.ToString(CultureInfo.InvariantCulture);for(int i=0;i<digits.Length;i++){var digit=new TextBlock{Text=digits[i].ToString(),FontSize=11.5};digit.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondaryBrush");host.Children.Add(digit);int delay=i*Motion.Stagger;RunLater(delay,delegate{if(digit.Parent==host)Motion.Enter(digit,Motion.Fast,0,4,1,0);});}}
 void UpdateBadge(int today){if(today==lastToday)return;lastToday=today;if(today<=0){if(todayBadge.Visibility==Visibility.Visible)Motion.Exit(todayBadge,delegate{if(lastToday==0)todayBadge.Visibility=Visibility.Collapsed;},150,4,-4,.98,0);return;}todayBadgeText.Text=today>99?"99+":today.ToString();todayBadge.Visibility=Visibility.Visible;Motion.Enter(todayBadge,250,-4,8,.96,0);}
 void SwapIcon(Button button,string value){if(Convert.ToString(button.Content)==value)return;button.Content=value;if(!Motion.Enabled)return;button.ApplyTemplate();ContentPresenter content=VisualChild<ContentPresenter>(button);if(content!=null)Motion.Enter(content,250,0,0,.96,0);}
 void AnimateDropdown(ComboBox combo){combo.ApplyTemplate();Popup popup=combo.Template.FindName("PART_Popup",combo) as Popup;Border border=popup==null?null:popup.Child as Border;ScrollViewer content=border==null?null:border.Child as ScrollViewer;if(content==null)return;Point screen=combo.PointToScreen(new Point(0,combo.ActualHeight));bool above=screen.Y+230>SystemParameters.WorkArea.Bottom;content.RenderTransformOrigin=new Point(.5,above?1:0);Motion.Enter(content,250,0,above?-6:6,.97,2);if(dropdownHooks.Add(border)){border.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(delegate(object sender,MouseButtonEventArgs e){ComboBoxItem item=Parent<ComboBoxItem>(e.OriginalSource as DependencyObject);if(item==null||!item.IsEnabled)return;e.Handled=true;combo.SelectedItem=item;CloseDropdown(combo,content,above);}),true);combo.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(combo.IsDropDownOpen&&(e.Key==Key.Escape||e.Key==Key.Enter)){e.Handled=true;CloseDropdown(combo,content,above);}};}}
 void CloseDropdown(ComboBox combo,ScrollViewer content,bool above){if(!combo.IsDropDownOpen)return;content.IsHitTestVisible=false;Motion.Exit(content,delegate{combo.IsDropDownOpen=false;content.IsHitTestVisible=true;},150,0,above?-4:4,.99,2);}
 void RunLater(int ms,Action action){if(ms<=0){action();return;}var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=delegate{timer.Stop();action();};timer.Start();}
 void ShowSuccess(){int version=++successVersion;if(successTimer!=null)successTimer.Stop();successToast.Visibility=Visibility.Visible;successPath.StrokeDashOffset=12;Motion.Enter(successToast,Motion.Medium,0,8,.96,0);Motion.Tween(Motion.Rotate(successToast),RotateTransform.AngleProperty,12,0,Motion.Medium);Motion.Tween(successPath,System.Windows.Shapes.Shape.StrokeDashOffsetProperty,12,0,Motion.Medium);successTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1400)};successTimer.Tick+=delegate{successTimer.Stop();if(version==successVersion)Motion.Exit(successToast,delegate{if(version==successVersion)successToast.Visibility=Visibility.Collapsed;},Motion.Quick,0,-8,.96,0);};successTimer.Start();}
 void ShowTitleError(){errorVersion++;if(errorTimer!=null)errorTimer.Stop();editorError.Visibility=Visibility.Visible;titleInputBorder.BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E15E66"));Motion.Shake(titleInputBorder);Motion.Enter(editorError,250,0,4,1,2);editTitle.Focus();errorTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};errorTimer.Tick+=delegate{errorTimer.Stop();ClearTitleError();};errorTimer.Start();}
 void ClearTitleError(){if(editorError==null||editorError.Visibility!=Visibility.Visible)return;int version=++errorVersion;if(errorTimer!=null)errorTimer.Stop();Motion.Exit(editorError,delegate{if(version==errorVersion)editorError.Visibility=Visibility.Collapsed;},280,0,-4,1,2);var current=titleInputBorder.BorderBrush as SolidColorBrush;var neutral=window.Resources["BorderBrush"] as SolidColorBrush;if(Motion.Enabled&&current!=null&&neutral!=null){var copy=current.Clone();titleInputBorder.BorderBrush=copy;var fade=new ColorAnimation(neutral.Color,TimeSpan.FromMilliseconds(280));fade.Completed+=delegate{if(version==errorVersion)titleInputBorder.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");};copy.BeginAnimation(SolidColorBrush.ColorProperty,fade);}else titleInputBorder.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");}
 void StartNotifications(){
  if(selfTest)return;
  notifyIcon=new Forms.NotifyIcon{Icon=Drawing.SystemIcons.Information,Text="流光日程",Visible=true};
  reminderTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
  reminderTimer.Tick+=delegate{CheckReminders();};
  reminderTimer.Start();
  window.Dispatcher.BeginInvoke(new Action(CheckReminders),DispatcherPriority.ApplicationIdle);
 }
 void CheckReminders(){
  var due=new List<TaskItem>();DateTime now=DateTime.Now;
  foreach(TaskItem t in tasks.Where(x=>!x.IsDone&&x.Reminder>=0&&!String.IsNullOrEmpty(x.DueTime)).ToList())try{TaskItem claimed=TaskStorage.ClaimReminder(taskPath,t.Id,now);if(claimed!=null){t.Notified=claimed.Notified;due.Add(claimed);}}catch(Exception ex){Log(ex);}
  if(due.Count==0)return;
  notifyIcon.BalloonTipTitle=due.Count==1?"日程提醒":"有 "+due.Count+" 项日程需要留意";
  notifyIcon.BalloonTipText=due.Count==1?due[0].Title+Environment.NewLine+due[0].DueTime+"  "+due[0].Location:due[0].Title+" 等日程已到提醒时间";
  notifyIcon.ShowBalloonTip(8000);
 }
 void SaveTasks(){if(loading||selfTest)return;try{TaskStorage.Save(taskPath,tasks);}catch(Exception ex){Log(ex);MessageBox.Show("日程保存失败，原有 tasks.json 未被覆盖。请检查文件权限与磁盘空间。","桌面日程");}}
 void LoadSettings(){try{WindowSettings s=File.Exists(settingsPath)?json.Deserialize<WindowSettings>(File.ReadAllText(settingsPath,Encoding.UTF8)):new WindowSettings();if(s.Width>=380)window.Width=s.Width;if(s.Height>=540)window.Height=s.Height;if(s.Left!=0||s.Top!=0){window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=s.Left;window.Top=s.Top;}window.Topmost=s.Topmost;pinButton=window.FindName("PinButton")as Button;if(pinButton!=null)pinButton.Content=window.Topmost?"◆":"◇";Color parsed;if(TryAccent(s.AccentColor,out parsed))accentColor=parsed;themeSelect.SelectedIndex=String.Equals(s.Theme,"Dark",StringComparison.OrdinalIgnoreCase)?1:0;opacitySlider.Value=s.Opacity>=55&&s.Opacity<=100?s.Opacity:80;ApplyAppearance();}catch(Exception ex){themeSelect.SelectedIndex=0;opacitySlider.Value=80;ApplyAppearance();Log(ex);}}
 static bool TryAccent(string value,out Color color){color=Colors.Transparent;if(String.IsNullOrEmpty(value)||value.Length!=7||value[0]!='#')return false;try{color=(Color)ColorConverter.ConvertFromString(value);return true;}catch{return false;}}
 void ScheduleSettings(object s,EventArgs e){if(selfTest||settingsTimer==null||window.WindowState!=WindowState.Normal)return;settingsTimer.Stop();settingsTimer.Start();}void SaveSettings(){if(selfTest)return;try{if(window.WindowState!=WindowState.Normal)return;File.WriteAllText(settingsPath,json.Serialize(new WindowSettings{Left=window.Left,Top=window.Top,Width=window.Width,Height=window.Height,Topmost=window.Topmost,Theme=themeSelect.SelectedIndex==1?"Dark":"Light",Opacity=opacitySlider.Value,AccentColor=String.Format("#{0:X2}{1:X2}{2:X2}",accentColor.R,accentColor.G,accentColor.B)}),new UTF8Encoding(false));}catch(Exception ex){Log(ex);}}void Log(Exception ex){try{File.AppendAllText(logPath,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+Environment.NewLine+ex+Environment.NewLine+Environment.NewLine,Encoding.UTF8);}catch{}}
 [DllImport("gdi32.dll")]static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
 [DllImport("user32.dll")]static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
 const int WM_NCHITTEST=0x84,HTLEFT=10,HTRIGHT=11,HTTOP=12,HTTOPLEFT=13,HTTOPRIGHT=14,HTBOTTOM=15,HTBOTTOMLEFT=16,HTBOTTOMRIGHT=17;IntPtr WindowProc(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg!=WM_NCHITTEST||window.WindowState==WindowState.Maximized)return IntPtr.Zero;long packed=l.ToInt64();int x=(short)(packed&65535),y=(short)((packed>>16)&65535);Point p=window.PointFromScreen(new Point(x,y));double z=8;bool a=p.X<z,b=p.X>window.ActualWidth-z,c=p.Y<z,d=p.Y>window.ActualHeight-z;int hit=a&&c?HTTOPLEFT:b&&c?HTTOPRIGHT:a&&d?HTBOTTOMLEFT:b&&d?HTBOTTOMRIGHT:a?HTLEFT:b?HTRIGHT:c?HTTOP:d?HTBOTTOM:0;if(hit!=0){handled=true;return new IntPtr(hit);}return IntPtr.Zero;}
}
internal static class Program
{
  [STAThread]static void Main(string[] args){
   if(args!=null&&args.Any(x=>String.Equals(x,"--data-test",StringComparison.OrdinalIgnoreCase))){Environment.ExitCode=RunDataTest();return;}
   bool hover=args!=null&&args.Any(x=>String.Equals(x,"--hover-test",StringComparison.OrdinalIgnoreCase));bool ui=args!=null&&args.Any(x=>String.Equals(x,"--ui-test",StringComparison.OrdinalIgnoreCase));bool preview=args!=null&&args.Any(x=>String.Equals(x,"--preview",StringComparison.OrdinalIgnoreCase));bool test=hover||ui||preview||(args!=null&&args.Any(x=>String.Equals(x,"--self-test",StringComparison.OrdinalIgnoreCase)));
   bool created;using(var m=new Mutex(true,@"Local\DesktopTodoWidget_Glass_1",out created)){if(!created){MessageBox.Show("流光日程已经在运行。","流光日程");return;}try{new DesktopTodoApp(test,hover,ui,preview).Run();}catch(Exception ex){string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"流光日程-错误日志.txt");try{File.AppendAllText(log,DateTime.Now+Environment.NewLine+ex+Environment.NewLine,Encoding.UTF8);}catch{}MessageBox.Show(ex is InvalidDataException?ex.Message:"程序启动失败，详情见错误日志。","流光日程");}}
  }
   static int RunDataTest(){
    string path=Path.GetTempFileName();try{
     string malformed="[{\"Title\":\"keep\"},null]";File.WriteAllText(path,malformed);
     bool rejected=false;try{TaskStorage.Read(path);}catch{rejected=true;}
     if(!rejected||File.ReadAllText(path)!=malformed)return 31;
     TaskStorage.Write(path,new[]{new TaskItem{Title="saved"}});
     if(TaskStorage.Read(path).Single().Title!="saved")return 32;
     DateTime now=DateTime.Now,missed=now.AddHours(-2),stale=now.AddHours(-26);
     var recent=new TaskItem{Title="missed",DueDate=missed.ToString("yyyy-MM-dd"),DueTime=missed.ToString("HH:mm"),Reminder=10};
     var old=new TaskItem{Title="stale",DueDate=stale.ToString("yyyy-MM-dd"),DueTime=stale.ToString("HH:mm"),Reminder=10};
     TaskStorage.Write(path,new[]{recent,old});
     if(TaskStorage.ClaimReminder(path,recent.Id,now)==null||TaskStorage.ClaimReminder(path,recent.Id,now)!=null||TaskStorage.ClaimReminder(path,old.Id,now)!=null)return 34;
     return 0;
    }catch{return 33;}finally{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp");}
   }
}
