using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;
using LibGit2Sharp;
using System.Collections.Generic;
using System.IO;

namespace UniTEAM {
	public class Console : EditorWindow {
		private string lastCommitMessage;
		public const float windowPadding = 5f;
		private float nextRefetch = -1;
		private float nextUncommittedChangesCompare = -1;
		private const float refetchFrequency = 15f;
		private const float compareFrequency = 1f;

		public Vector2 overviewWindowScroll;
		public Vector2 updatesOnServerWindowScroll;
		public Vector2 uncommitedChangesWindowScroll;
		public Vector2 localStashedCommitsWindowScroll;

		public ChangesetViewWindow changesetViewWindow;
		public HistoryWindow historyWindow;
		public LocalStashedCommitsWindow localStashedCommitsWindow;
		public OverviewWindow overviewWindow;
		public SetupWindow setupWindow;
		public UncommitedChangesWindow uncommitedChangesWindow;
		public UpdatesOnServerWindow updatesOnServerWindow;

		public Repository repo;
		public Remote remote;
		public Branch branch;
		public Credentials credentials;
		public bool isFetchComplete = false;
		public bool isOkToPoll = false;
		public IEnumerable<Commit> commitsOnServer = new BindingList<Commit>();
		public IEnumerable<Commit> commitsInStash = new BindingList<Commit>();
		private bool isConsoleReady = false;
		public WindowSet windowSet;
		public ConfigManager configManager;

		public enum WindowSet {
			overview,
			commits,
			history,
			setup
		}

		[MenuItem( "Window/Git UniTEAM Console" )]
		private static void init() {
			EditorWindow.GetWindow( typeof( Console ), false, "UniTEAM" );
		}

		private void OnEnable() {
			configManager = new ConfigManager( this );

			try {
				repo = new Repository( configManager.explicitPathToRepository ?? Directory.GetCurrentDirectory() );
				branch = repo.Head;
				remote = repo.Network.Remotes[ "origin" ];
			} catch ( System.Exception e ) {
				Debug.Log( "Repo not found: " + e );
				changeWindow( WindowSet.setup );
				isConsoleReady = true;
				return;
			}

			changeWindow( WindowSet.overview );

			isConsoleReady = true;
		}

		private void OnDisable() {
			isFetchComplete = false;
		}

		private void changeWindow( WindowSet windowSet ) {
			this.windowSet = windowSet;
		}

		public void fetch() {
			//commitsOnServer = repo.Commits.QueryBy( new Filter { Since = branch.TrackedBranch, Until = branch.Tip } );
			//commitsInStash = repo.Commits.QueryBy( new Filter { Since = branch.Tip, Until = branch.TrackedBranch } );
			Debug.Log( "Fetch called..." );
			try {
				FetchHelper.RemoteFetch( remote, credentials, this );

				branch = repo.Head;

				nextRefetch = Time.realtimeSinceStartup + refetchFrequency;

				Repaint();
			} catch ( System.Exception e ) {
				Debug.Log( e );
			}
		}

		private void OnInspectorUpdate() {
			if ( !isConsoleReady ) {
				return;
			}

			if ( repo == null ) {
				return;
			}

			if ( Time.realtimeSinceStartup >= nextRefetch ) {
				if ( windowSet == WindowSet.commits ) {
					fetch();
				}
			}

			if ( Time.realtimeSinceStartup >= nextUncommittedChangesCompare ) {
				if ( uncommitedChangesWindow != null ) {
					uncommitedChangesWindow.reset( repo.Diff.Compare(), this );
					nextUncommittedChangesCompare = Time.realtimeSinceStartup + compareFrequency;
				}
			}
		}

		private void createWindowInstances() {
			changesetViewWindow = new ChangesetViewWindow();
			historyWindow = new HistoryWindow();
			localStashedCommitsWindow = new LocalStashedCommitsWindow();
			overviewWindow = new OverviewWindow();
			setupWindow = new SetupWindow();
			uncommitedChangesWindow = new UncommitedChangesWindow();
			updatesOnServerWindow = new UpdatesOnServerWindow();
		}

		private void OnGUI() {
			if ( !isConsoleReady ) {
				return;
			}

			try {
				//# Create new instances so we can instantia the guiskin stuff once and only once
				//# reducing the amount of function calls during ongui
				if ( uncommitedChangesWindow == null ) {
					createWindowInstances();
					uncommitedChangesWindow.reset( repo.Diff.Compare(), this );
				}

				fixWindowRects();

				GUILayout.BeginHorizontal( GUILayout.Width( Screen.width / 4 ) );

				if ( GUILayout.Button( "Overview" ) ) {
					changeWindow( WindowSet.overview );
				}

				if ( GUILayout.Button( "Commit Manager" ) ) {
					changeWindow( WindowSet.commits );
				}

				if ( GUILayout.Button( "Repo History" ) ) {
					changeWindow( WindowSet.history );
				}

				if ( GUILayout.Button( "Force Re-fetch (refresh): " ) ) {
					fetch();
				}

				GUI.enabled = !localStashedCommitsWindow.isPushing;
				if ( GUILayout.Button( ( !localStashedCommitsWindow.isPushing ) ? "Push Stashed Commits" : "Pushing, please wait..." ) ) {
					//# Don't send blank pushes....
					if ( repo.Head.AheadBy == 0 ) {
						return;
					}

					UnityThreadHelper.CreateThread( () => {
						localStashedCommitsWindow.isPushing = true;
						repo.Network.Push( remote, "refs/heads/master:refs/heads/master", OnPushStatusError, credentials );
						localStashedCommitsWindow.isPushing = false;
					} );
				}
				GUI.enabled = true;

				GUILayout.EndHorizontal();

				BeginWindows();
				if ( !currentError.Equals( string.Empty ) ) {
					GUILayout.Window( 4, currentErrorLocation, errorWindow, "Error:" );
				} else {
					switch ( windowSet ) {
						case WindowSet.overview:
							GUILayout.Window( 0, overviewWindow.rect, windowDelegate, "Overview" );
							GUILayout.Window( 1, uncommitedChangesWindow.rect, windowDelegate, "Uncommited Changes" );
							break;
						case WindowSet.commits:
							GUILayout.Window( 4, changesetViewWindow.rect, windowDelegate, "Changeset Viewer" );
							GUILayout.Window( 2, updatesOnServerWindow.rect, windowDelegate, "Updates on Server [Commits Behind: " + repo.Head.BehindBy + "]" );
							GUILayout.Window( 3, localStashedCommitsWindow.rect, windowDelegate, "Local Commit Stash [Commits Ahead: " + repo.Head.AheadBy + "]" );
							break;
						case WindowSet.history:
							GUILayout.Window( 4, changesetViewWindow.rect, windowDelegate, "Changeset Viewer" );
							GUILayout.Window( 5, historyWindow.rect, windowDelegate, "Repository History" );
							GUILayout.Window( 6, historyWindow.commitMessageRect, windowDelegate, "Commit Messages" );
							break;
						case WindowSet.setup:
							GUILayout.Window( 7, setupWindow.rect, windowDelegate, "Git UniTEAM Initial Setup" );
							break;
					}
				}

				EndWindows();
			} catch ( System.Exception e ) {
				Debug.Log( e );
			}
		}

		//# Using this to pass the console reference. Trying not to leak stuff with static properties...
		private void windowDelegate( int id ) {
			switch ( id ) {
				case 0:
					overviewWindow.draw( this, id );
					break;
				case 1:
					uncommitedChangesWindow.draw( this, id );
					break;
				case 2:
					updatesOnServerWindow.draw( this, id );
					break;
				case 3:
					localStashedCommitsWindow.draw( this, id );
					break;
				case 4:
					changesetViewWindow.draw( this, id );
					break;
				case 5:
					historyWindow.draw( this, id );
					break;
				case 6:
					historyWindow.commitMessageWindow( id );
					break;
				case 7:
					setupWindow.draw( this, id );
					break;
			}
		}

		private void OnPushStatusError( PushStatusError pushStatusErrors ) {
			Debug.LogError( "Push errors: " + pushStatusErrors );
		}

		public string currentError = string.Empty;
		public Rect currentErrorLocation;

		private void errorWindow( int i ) {
			GUILayout.Label( currentError );
			if ( GUI.Button( new Rect( 0, currentErrorLocation.height - 20, currentErrorLocation.width, 20 ), "Close" ) ) {
				currentError = string.Empty;
			}
		}

		private void fixWindowRects() {
			float positionFromTop = 30f;
			float windowWidth = ( position.width / 2f ) - ( windowPadding * 2 );
			float windowHeight = ( position.height ) - positionFromTop - ( windowPadding * 2 );

			overviewWindow.rect = new Rect(
				windowPadding,
				positionFromTop,
				windowWidth,
				windowHeight );

			uncommitedChangesWindow.rect = new Rect(
				windowPadding + windowWidth + ( windowPadding * 2 ),
				positionFromTop,
				windowWidth,
				windowHeight );

			changesetViewWindow.rect = overviewWindow.rect;

			historyWindow.rect = uncommitedChangesWindow.rect;
			historyWindow.rect.height = ( windowHeight / 1.25f ) - ( windowPadding * 2 );

			historyWindow.commitMessageRect = new Rect(
				historyWindow.rect.x,
				historyWindow.rect.y + historyWindow.rect.height + ( windowPadding * 2 ),
				historyWindow.rect.width,
				( historyWindow.rect.height / 4f ) );

			updatesOnServerWindow.rect = new Rect(
				windowPadding + windowWidth + ( windowPadding * 2 ),
				positionFromTop,
				windowWidth,
				windowHeight / 2 - ( windowPadding * 2 ) );

			localStashedCommitsWindow.rect = new Rect(
				windowPadding + windowWidth + ( windowPadding * 2 ),
				positionFromTop + ( windowHeight / 2 ) + ( windowPadding * 2 ),
				windowWidth,
				windowHeight / 2 - ( windowPadding * 2 ) );

			setupWindow.rect = new Rect(
				( position.width / 2 ) - ( windowWidth / 2 ),
				positionFromTop,
				windowWidth,
				windowHeight / 2 );
		}

		public delegate void onCommitSelected( Commit commit );

		public void getUpdateItem( Commit commit, Commit lastCommit, Rect windowRect, onCommitSelected onCommitSelected ) {
			CommitItem item = new CommitItem( commit );

			float horizontalWidth = ( windowRect.width ) - ( windowPadding * 2 ) - 25;
			float halfWidth = ( horizontalWidth / 2 ) - ( windowPadding * 2 );
			float quarterWidth = ( horizontalWidth / 4 ) - ( windowPadding * 2 );

			Rect r = EditorGUILayout.BeginHorizontal( "Button", GUILayout.Width( horizontalWidth ) );

			if ( GUI.Button( r, GUIContent.none ) ) {
				try {
					changesetViewWindow.reset( repo.Diff.Compare( lastCommit.Tree, commit.Tree ), this );
				} catch ( System.Exception e ) {
					changesetViewWindow.reset( e );
				} finally {
					onCommitSelected( commit );
				}
			}

			GUILayout.Label( item.commitMessage.Substring( 0, Mathf.Min( item.commitMessage.Length, 100 ) ), GUILayout.Width( halfWidth ) );
			GUILayout.Label( "\t\t" + commit.Author.Name, GUILayout.Width( quarterWidth ) );
			GUILayout.Label( item.dateString, GUILayout.Width( quarterWidth ) );

			GUILayout.EndHorizontal();
		}
	}
}
