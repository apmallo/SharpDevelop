// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="Mike Krger" email="mike@icsharpcode.net"/>
//     <version value="$version"/>
// </file>

using System;
using System.IO;
using System.Collections.Generic;

using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Dom;

namespace ICSharpCode.Core
{
	public class TaskService
	{
		static List<Task>          tasks                    = new List<Task>();
		static MessageViewCategory buildMessageViewCategory = new MessageViewCategory("Build", "${res:MainWindow.Windows.OutputWindow.BuildCategory}");
		
		public static MessageViewCategory BuildMessageViewCategory {
			get {
				return buildMessageViewCategory;
			}
		}
		
		public class TaskEnumerator {
			public IEnumerator<Task> GetEnumerator()
			{
				foreach (Task task in tasks) {
					if (task.TaskType != TaskType.Comment) {
						yield return task;
					}
				}
			}
		}
		
		public static int TaskCount {
			get {
				return tasks.Count - GetCount(TaskType.Comment);
			}
		}
		public static TaskEnumerator Tasks {
			get {
				return new TaskEnumerator();
			}
		}
		
		public class CommentTaskEnumerator {
			public IEnumerator<Task> GetEnumerator()
			{
				foreach (Task task in tasks) {
					if (task.TaskType == TaskType.Comment) {
						yield return task;
					}
				}
			}
		}
		public static CommentTaskEnumerator CommentTasks {
			get {
				return new CommentTaskEnumerator();
			}
		}
		
		static Dictionary<TaskType, int> taskCount = new Dictionary<TaskType, int>();
		
		public static int GetCount(TaskType type)
		{
			if (!taskCount.ContainsKey(type)) {
				return 0;
			}
			return taskCount[type];
		}
		
		public static bool SomethingWentWrong {
			get {
				return GetCount(TaskType.Error) + GetCount(TaskType.Warning) > 0;
			}
		}
		
		public static bool HasCriticalErrors(bool treatWarningsAsErrors)
		{
			if (treatWarningsAsErrors) {
				return SomethingWentWrong;
			} else {
				return GetCount(TaskType.Error) > 0;
			}
		}
		
		static TaskService()
		{
			FileService.FileRenamed += new FileRenameEventHandler(CheckFileRename);
			FileService.FileRemoved += new FileEventHandler(CheckFileRemove);
			
			ProjectService.SolutionClosed += new EventHandler(ProjectServiceSolutionClosed);
		}
		
		static void ProjectServiceSolutionClosed(object sender, EventArgs e)
		{
			Clear();
		}
		
		static void CheckFileRemove(object sender, FileEventArgs e)
		{
			for (int i = 0; i < tasks.Count; ++i) {
				Task curTask = tasks[i];
				if (FileUtility.IsEqualFile(curTask.FileName, e.FileName)) {
					Remove(curTask);
					--i;
				}
			}
		}
		
		static void CheckFileRename(object sender, FileRenameEventArgs e)
		{
			for (int i = 0; i < tasks.Count; ++i) {
				Task curTask = tasks[i];
				if (FileUtility.IsEqualFile(curTask.FileName, e.SourceFile)) {
					Remove(curTask);
					curTask.FileName = Path.GetFullPath(e.TargetFile);
					Add(curTask);
					--i;
				}
			}
		}
		
		public static void Clear()
		{
			taskCount.Clear();
			tasks.Clear();
			OnCleared(EventArgs.Empty);
		}
		
		public static void Add(Task task)
		{
			tasks.Add(task);
			if (!taskCount.ContainsKey(task.TaskType)) {
				taskCount[task.TaskType] = 1;
			} else {
				taskCount[task.TaskType]++;
			}
			OnAdded(new TaskEventArgs(task));
		}
		
		public static void Remove(Task task)
		{
			if (tasks.Contains(task)) {
				tasks.Remove(task);
				taskCount[task.TaskType]--;
				OnRemoved(new TaskEventArgs(task));
			}
		}
		
		public static void UpdateCommentTags(string fileName, List<Tag> tagComments)
		{
			if (fileName == null || tagComments == null) {
				return;
			}
			WorkbenchSingleton.SafeThreadAsyncCall(typeof(TaskService), "UpdateCommentTagsInvoked", fileName, tagComments);
		}
		
		static void UpdateCommentTagsInvoked(string fileName, List<Tag> tagComments)
		{
			List<Task> newTasks = new List<Task>();
			foreach (Tag tag in tagComments) {
				newTasks.Add(new Task(fileName,
				                      tag.Key + tag.CommentString,
				                      tag.Region.BeginColumn,
				                      tag.Region.BeginLine,
				                      TaskType.Comment));
			}
			List<Task> oldTasks = new List<Task>();
			
			foreach (Task task in CommentTasks) {
				if (FileUtility.IsEqualFile(task.FileName, fileName)) {
					oldTasks.Add(task);
				}
			}
			
			for (int i = 0; i < newTasks.Count; ++i) {
				for (int j = 0; j < oldTasks.Count; ++j) {
					if (oldTasks[j] != null &&
					    newTasks[i].Line        == oldTasks[j].Line &&
					    newTasks[i].Column      == oldTasks[j].Column &&
					    newTasks[i].Description == oldTasks[j].Description)
					{
						newTasks[i] = null;
						oldTasks[j] = null;
						break;
					}
				}
			}
			
			foreach (Task task in newTasks) {
				if (task != null) {
					Add(task);
				}
			}
			
			foreach (Task task in oldTasks) {
				if (task != null) {
					Remove(task);
				}
			}
		}

		static void OnCleared(EventArgs e)
		{
			if (Cleared != null) {
				Cleared(null, e);
			}
		}
		
		static void OnAdded(TaskEventArgs e)
		{
			if (Added != null) {
				Added(null, e);
			}
		}
		
		static void OnRemoved(TaskEventArgs e)
		{
			if (Removed != null) {
				Removed(null, e);
			}
		}
		
		public static event TaskEventHandler Added;
		public static event TaskEventHandler Removed;
		public static event EventHandler     Cleared;
	}

}
