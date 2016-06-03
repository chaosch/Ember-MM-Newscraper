﻿' ################################################################################
' #                             EMBER MEDIA MANAGER                              #
' ################################################################################
' ################################################################################
' # This file is part of Ember Media Manager.                                    #
' #                                                                              #
' # Ember Media Manager is free software: you can redistribute it and/or modify  #
' # it under the terms of the GNU General Public License as published by         #
' # the Free Software Foundation, either version 3 of the License, or            #
' # (at your option) any later version.                                          #
' #                                                                              #
' # Ember Media Manager is distributed in the hope that it will be useful,       #
' # but WITHOUT ANY WARRANTY; without even the implied warranty of               #
' # MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the                #
' # GNU General Public License for more details.                                 #
' #                                                                              #
' # You should have received a copy of the GNU General Public License            #
' # along with Ember Media Manager.  If not, see <http://www.gnu.org/licenses/>. #
' ################################################################################

Imports NLog

Public Class TaskManager

#Region "Fields"

    Shared logger As Logger = LogManager.GetCurrentClassLogger()

    Private TaskList As New Queue(Of TaskItem)

    Friend WithEvents bwTaskManager As New System.ComponentModel.BackgroundWorker

#End Region 'Fields

#Region "Events"

    Public Event ProgressUpdate(ByVal eProgressValue As ProgressStatus)
    Public Event TaskManagerDone()

#End Region 'Events

#Region "Properties"

    ReadOnly Property IsBusy() As Boolean
        Get
            Return bwTaskManager.IsBusy
        End Get
    End Property

#End Region 'Properties

#Region "Methods"

    Public Sub AddTask(ByVal tTaskItem As TaskItem)
        TaskList.Enqueue(tTaskItem)
        RaiseEvent ProgressUpdate(New ProgressStatus With {
                                  .EventType = Enums.TaskManagerEventType.MainTaskUpdate,
                                  .MainTaskProgressbar = New ProgressStatus.ProgressBar With {
                                  .Maximum = TaskList.Count + 1,
                                  .Style = Windows.Forms.ProgressBarStyle.Continuous,
                                  .Value = 1}})

        If Not bwTaskManager.IsBusy Then
            RunTaskManager()
        Else
            'ChangeTaskManagerStatus(lblTaskManagerStatus, String.Concat("Pending Tasks: ", (TaskList.Count + 1).ToString))
        End If
    End Sub

    Private Sub bwTaskManager_DoWork(ByVal sender As Object, ByVal e As System.ComponentModel.DoWorkEventArgs) Handles bwTaskManager.DoWork
        While TaskList.Count > 0
            If bwTaskManager.CancellationPending Then Return

            Dim currTask As TaskItem = TaskList.Dequeue()

            Select Case currTask.TaskType
                Case Enums.TaskType.CleanFiles
                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .EventType = Enums.TaskManagerEventType.MainTaskUpdate,
                                                     .MainTaskMessage = "Clean Files",
                                                     .MainTaskProgressbar = New ProgressStatus.ProgressBar With {
                                                     .Maximum = TaskList.Count + 1,
                                                     .Style = Windows.Forms.ProgressBarStyle.Continuous,
                                                     .Value = 1}})
                    Task_CleanFiles(currTask)

                Case Enums.TaskType.CopyBackdrops
                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .EventType = Enums.TaskManagerEventType.MainTaskUpdate,
                                                     .MainTaskMessage = "Copy Backdrops",
                                                     .MainTaskProgressbar = New ProgressStatus.ProgressBar With {
                                                     .Maximum = TaskList.Count + 1,
                                                     .Style = Windows.Forms.ProgressBarStyle.Continuous,
                                                     .Value = 1}})
                    Task_CopyBackdrops(currTask)

                Case Enums.TaskType.SetWatchedState
                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .EventType = Enums.TaskManagerEventType.MainTaskUpdate,
                                                     .MainTaskMessage = "Set Watched State",
                                                     .MainTaskProgressbar = New ProgressStatus.ProgressBar With {
                                                     .Maximum = TaskList.Count + 1,
                                                     .Style = Windows.Forms.ProgressBarStyle.Continuous,
                                                     .Value = 1}})
                    Using SQLtransaction As SQLite.SQLiteTransaction = Master.DB.MyVideosDBConn.BeginTransaction()
                        Task_SetWatchedState(currTask)
                        SQLtransaction.Commit()
                    End Using
            End Select
        End While
        RaiseEvent ProgressUpdate(New ProgressStatus With {
                                  .EventType = Enums.TaskManagerEventType.MainTaskUpdate,
                                  .MainTaskProgressbar = New ProgressStatus.ProgressBar With {
                                  .Maximum = 1,
                                  .Style = Windows.Forms.ProgressBarStyle.Continuous,
                                  .Value = 1}})
    End Sub

    Private Sub bwTaskManager_ProgressChanged(ByVal sender As Object, ByVal e As System.ComponentModel.ProgressChangedEventArgs) Handles bwTaskManager.ProgressChanged
        RaiseEvent ProgressUpdate(DirectCast(e.UserState, ProgressStatus))
    End Sub

    Private Sub bwTaskManager_RunWorkerCompleted(ByVal sender As Object, ByVal e As System.ComponentModel.RunWorkerCompletedEventArgs) Handles bwTaskManager.RunWorkerCompleted
        RaiseEvent TaskManagerDone()
    End Sub

    Public Sub Cancel()
        If bwTaskManager.IsBusy Then bwTaskManager.CancelAsync()
    End Sub

    Public Sub CancelAndWait()
        If bwTaskManager.IsBusy Then bwTaskManager.CancelAsync()
        While bwTaskManager.IsBusy
            Threading.Thread.Sleep(50)
        End While
    End Sub

    Private Sub RunTaskManager()
        While bwTaskManager.IsBusy
            Threading.Thread.Sleep(50)
        End While
        bwTaskManager = New System.ComponentModel.BackgroundWorker
        bwTaskManager.WorkerReportsProgress = True
        bwTaskManager.WorkerSupportsCancellation = True
        bwTaskManager.RunWorkerAsync()
    End Sub

    Private Sub Task_CleanFiles(ByVal tTaskItem As TaskItem)
        FileUtils.CleanUp.DoCleanUp(tTaskItem.ContentType)
    End Sub

    Private Sub Task_CopyBackdrops(ByVal tTaskItem As TaskItem)
        Select Case tTaskItem.ContentType
            Case Enums.ContentType.Movie
                Using SQLcommand As SQLite.SQLiteCommand = Master.DB.MyVideosDBConn.CreateCommand()
                    SQLcommand.CommandText = "SELECT ListTitle, FanartPath FROM movielist WHERE FanartPath IS NOT NULL AND NOT FanartPath='' ORDER BY ListTitle;"
                    Using SQLreader As SQLite.SQLiteDataReader = SQLcommand.ExecuteReader()
                        While SQLreader.Read
                            If bwTaskManager.CancellationPending Then Return
                            bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                             .EventType = Enums.TaskManagerEventType.SubTaskUpdate,
                                             .SubTaskMessage = SQLreader("ListTitle").ToString})

                            FileUtils.Common.CopyFanartToBackdropsPath(SQLreader("FanartPath").ToString, tTaskItem.ContentType)
                        End While
                    End Using
                End Using

            Case Enums.ContentType.TVShow
                Using SQLcommand As SQLite.SQLiteCommand = Master.DB.MyVideosDBConn.CreateCommand()
                    SQLcommand.CommandText = "SELECT ListTitle, FanartPath FROM tvshowlist WHERE FanartPath IS NOT NULL AND NOT FanartPath='' ORDER BY ListTitle;"
                    Using SQLreader As SQLite.SQLiteDataReader = SQLcommand.ExecuteReader()
                        While SQLreader.Read
                            If bwTaskManager.CancellationPending Then Return
                            bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                             .EventType = Enums.TaskManagerEventType.SubTaskUpdate,
                                             .SubTaskMessage = SQLreader("ListTitle").ToString})

                            FileUtils.Common.CopyFanartToBackdropsPath(SQLreader("FanartPath").ToString, tTaskItem.ContentType)
                        End While
                    End Using
                End Using
        End Select
    End Sub

    Private Sub Task_SetWatchedState(ByVal tTaskItem As TaskItem)
        Select Case tTaskItem.ContentType

            Case Enums.ContentType.Movie
                For Each tID In tTaskItem.Container_WatchedState.IDs
                    If bwTaskManager.CancellationPending Then Return
                    Dim tmpDBElement As Database.DBElement = Master.DB.Load_Movie(tID)

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .EventType = Enums.TaskManagerEventType.SubTaskUpdate,
                                                     .SubTaskMessage = tmpDBElement.Movie.Title})

                    If tTaskItem.Container_WatchedState.SetWached Then
                        tmpDBElement.Movie.LastPlayed = If(tmpDBElement.Movie.LastPlayedSpecified, tmpDBElement.Movie.LastPlayed, Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                        tmpDBElement.Movie.PlayCount = If(tmpDBElement.Movie.PlayCountSpecified, tmpDBElement.Movie.PlayCount, 1)
                    Else
                        tmpDBElement.Movie.LastPlayed = String.Empty
                        tmpDBElement.Movie.PlayCount = 0
                    End If

                    Master.DB.Save_Movie(tmpDBElement, True, True, False, False)

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                 .ContentType = Enums.ContentType.Movie,
                                                 .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                 .ID = tmpDBElement.ID})
                Next

            Case Enums.ContentType.TVEpisode
                For Each tID In tTaskItem.Container_WatchedState.IDs
                    If bwTaskManager.CancellationPending Then Return
                    Dim tmpDBElement As Database.DBElement = Master.DB.Load_TVEpisode(tID, True)

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                         .EventType = Enums.TaskManagerEventType.SubTaskUpdate,
                                                         .SubTaskMessage = tmpDBElement.TVEpisode.Title})

                    If tTaskItem.Container_WatchedState.SetWached Then
                        tmpDBElement.TVEpisode.LastPlayed = If(tmpDBElement.TVEpisode.LastPlayedSpecified, tmpDBElement.TVEpisode.LastPlayed, Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                        tmpDBElement.TVEpisode.Playcount = If(tmpDBElement.TVEpisode.PlaycountSpecified, tmpDBElement.TVEpisode.Playcount, 1)
                    Else
                        tmpDBElement.TVEpisode.LastPlayed = String.Empty
                        tmpDBElement.TVEpisode.Playcount = 0
                    End If

                    Master.DB.Save_TVEpisode(tmpDBElement, True, True, False, False, True)

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                 .ContentType = Enums.ContentType.TVEpisode,
                                                 .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                 .ID = tmpDBElement.ID})
                Next

            Case Enums.ContentType.TVSeason
                For Each tID In tTaskItem.Container_WatchedState.IDs
                    If bwTaskManager.CancellationPending Then Return
                    Dim tmpDBElement_TVSeason As Database.DBElement = Master.DB.Load_TVSeason(tID, True, True)
                    For Each tmpDBElement As Database.DBElement In tmpDBElement_TVSeason.Episodes.OrderBy(Function(f) f.TVEpisode.Episode)
                        If bwTaskManager.CancellationPending Then Exit For
                        bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .EventType = Enums.TaskManagerEventType.SubTaskUpdate,
                                                     .SubTaskMessage = tmpDBElement.TVEpisode.Title})

                        If tTaskItem.Container_WatchedState.SetWached Then
                            tmpDBElement.TVEpisode.LastPlayed = If(tmpDBElement.TVEpisode.LastPlayedSpecified, tmpDBElement.TVEpisode.LastPlayed, Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                            tmpDBElement.TVEpisode.Playcount = If(tmpDBElement.TVEpisode.PlaycountSpecified, tmpDBElement.TVEpisode.Playcount, 1)
                        Else
                            tmpDBElement.TVEpisode.LastPlayed = String.Empty
                            tmpDBElement.TVEpisode.Playcount = 0
                        End If

                        Master.DB.Save_TVEpisode(tmpDBElement, True, True, False, False, True)

                        bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .ContentType = Enums.ContentType.TVEpisode,
                                                     .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                     .ID = tmpDBElement.ID})
                    Next

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .ContentType = Enums.ContentType.TVSeason,
                                                     .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                     .ID = tmpDBElement_TVSeason.ID})

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .ContentType = Enums.ContentType.TVShow,
                                                     .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                     .ID = tmpDBElement_TVSeason.ShowID})
                Next

            Case Enums.ContentType.TVShow
                For Each tID In tTaskItem.Container_WatchedState.IDs
                    If bwTaskManager.CancellationPending Then Return
                    Dim tmpDBElement_TVShow As Database.DBElement = Master.DB.Load_TVShow(tID, True, True)
                    For Each tmpDBElement As Database.DBElement In tmpDBElement_TVShow.Episodes.OrderBy(Function(f) f.TVEpisode.Season).OrderBy(Function(f) f.TVEpisode.Episode)
                        If bwTaskManager.CancellationPending Then Exit For
                        bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .EventType = Enums.TaskManagerEventType.SubTaskUpdate,
                                                     .SubTaskMessage = tmpDBElement.TVEpisode.Title})

                        If tTaskItem.Container_WatchedState.SetWached Then
                            tmpDBElement.TVEpisode.LastPlayed = If(tmpDBElement.TVEpisode.LastPlayedSpecified, tmpDBElement.TVEpisode.LastPlayed, Date.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                            tmpDBElement.TVEpisode.Playcount = If(tmpDBElement.TVEpisode.PlaycountSpecified, tmpDBElement.TVEpisode.Playcount, 1)
                        Else
                            tmpDBElement.TVEpisode.LastPlayed = String.Empty
                            tmpDBElement.TVEpisode.Playcount = 0
                        End If

                        Master.DB.Save_TVEpisode(tmpDBElement, True, True, False, False, True)

                        bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .ContentType = Enums.ContentType.TVEpisode,
                                                     .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                     .ID = tmpDBElement.ID})
                    Next

                    For Each tSeason In tmpDBElement_TVShow.Seasons.OrderBy(Function(f) f.TVSeason.Season)
                        bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                         .ContentType = Enums.ContentType.TVSeason,
                                                         .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                         .ID = tSeason.ID})
                    Next

                    bwTaskManager.ReportProgress(-1, New ProgressStatus With {
                                                     .ContentType = Enums.ContentType.TVShow,
                                                     .EventType = Enums.TaskManagerEventType.RefreshRow,
                                                     .ID = tmpDBElement_TVShow.ID})
                Next

        End Select
    End Sub

#End Region 'Methods

#Region "Nested Types"

    Public Structure ProgressStatus

#Region "Fields"

        Dim ContentType As Enums.ContentType
        Dim EventType As Enums.TaskManagerEventType
        Dim ID As Long
        Dim MainTaskProgressbar As ProgressBar
        Dim MainTaskMessage As String
        Dim SubTaskProgressbar As ProgressBar
        Dim SubTaskMessage As String

#End Region 'Fields

#Region "Nested Types"

        Public Structure ProgressBar

#Region "Fields"

            Dim Maximum As Integer
            Dim Minimum As Integer
            Dim Style As Windows.Forms.ProgressBarStyle
            Dim Value As Integer

#End Region 'Fields

        End Structure

#End Region 'Nested Types

    End Structure

    Public Structure TaskItem

#Region "Fields"

        Dim ContentType As Enums.ContentType
        Dim Container_Scan As ScanContainer
        Dim Container_Scrape As ScrapeContainer
        Dim Container_WatchedState As WatchedStateContainer
        Dim TaskType As Enums.TaskType

#End Region

#Region "Nested Types"

        Public Structure ScanContainer

#Region "Fields"

            Dim ID As Long
            Dim Options As Structures.ScanOrClean
            Dim SpecifiedPath As String

#End Region 'Fields

        End Structure

        Public Structure ScrapeContainer

#Region "Fields"

            Dim IDs As List(Of Long)
            Dim Modifiers As Structures.ScrapeModifiers
            Dim Options As Structures.ScrapeOptions

#End Region 'Fields

        End Structure

        Public Structure WatchedStateContainer

#Region "Fields"

            Dim IDs As List(Of Long)
            Dim SetWached As Boolean

#End Region 'Fields

        End Structure

#End Region 'Nested Types

    End Structure

#End Region

End Class
