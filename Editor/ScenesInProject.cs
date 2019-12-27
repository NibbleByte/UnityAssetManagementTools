using System.IO;
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

namespace DevLocker.Tools.AssetsManagement
{
	/// <summary>
	/// Window with a list of all available scenes in the project:
	/// + quick access to scenes
	/// + easily load scenes additively
	/// + pin favourites
	/// 
	/// Initial version of the script: http://wiki.unity3d.com/index.php/SceneViewWindow by Kevin Tarchenski.
	/// Advanced (this) version by Filip Slavov (a.k.a. NibbleByte) - NibbleByte3@gmail.com.
	/// 
	/// Usage: "Tools/Asset Management/Scenes In Project"
	/// </summary>
	public class ScenesInProject : EditorWindow
	{
		[MenuItem("Tools/Assets Management/Scenes In Project")]
		private static void Init()
		{
			var window = (ScenesInProject)GetWindow(typeof(ScenesInProject), false, "Scenes In Project");
			window.position = new Rect(window.position.xMin + 100f, window.position.yMin + 100f, 300f, 400f);
		}

		//
		// Add here paths that you want to exclude from the list (example, demos etc from plugins).
		//
		public static string[] excludeScenePaths = new string[] {
			"Assets/Extensions/"
		};


		private enum PinnedOptions
		{
			Unpin,
			MoveUp,
			MoveDown,
			MoveFirst,
			MoveLast,
			ShowInExplorer,
			ShowInProject,
		}

		private readonly char[] FILTER_WORD_SEPARATORS = new char[] { ' ', '\t' };
		private GUIStyle LEFT_ALIGNED_BUTTON;
		private GUIContent m_SceneButtonTextCache = new GUIContent();
		private GUIContent m_AddSceneButtonTextCache = new GUIContent("+", "Load scene additively");
		private GUIContent m_ActiveSceneButtonTextCache = new GUIContent("*", "Active scene (cannot unload)");
		private GUIContent m_RemoveSceneButtonTextCache = new GUIContent("-", "Unload scene");

		public static bool AssetsChanged = false;


		private bool m_Initialized = false;

		// In big projects with 1k number of scenes, don't show everything.
		[NonSerialized]
		private bool m_ShowFullBigList = false;
		private const int SANITY_LIST_COUNT = 64; // Round number... :D

		private Vector2 m_ScrollPos;
		private Vector2 m_ScrollPosPinned;
		private string m_Filter = string.Empty;
		private bool m_FocusFilterField = false; // HACK!

		private List<string> m_Pinned = new List<string>();
		private List<string> m_Scenes = new List<string>();


		//
		// Registry setting name
		// Create individual setting per project (and project copies), to avoid clashes and bugs.
		//
		private string PinnedScenesSetting {
			get {
				return string.Format("SceneView_PinnedScenes_{0}", Regex.Replace(Application.dataPath, @"[/\\:?]", ""));
			}
		}

		private string ScenesSetting {
			get {
				return string.Format("SceneView_Scenes_{0}", Regex.Replace(Application.dataPath, @"[/\\:?]", ""));
			}
		}

		private void StoreListToPrefs(string setting, List<string> list)
		{
			string storage = string.Join(";", list.ToArray());
			EditorPrefs.SetString(setting, storage);
		}

		private List<string> ReadListFromPrefs(string setting)
		{
			List<string> result;
			string storage = EditorPrefs.GetString(setting, string.Empty);

			if (!string.IsNullOrEmpty(storage)) {
				result = new List<string>(storage.Split(';'));
			} else {
				result = new List<string>();
			}
			return result;
		}

		private void StorePinned()
		{
			StoreListToPrefs(PinnedScenesSetting, m_Pinned);
		}

		private void StoreScenes()
		{
			StoreListToPrefs(ScenesSetting, m_Scenes);
		}

		private bool RemoveRedundant(List<string> list, List<string> scenesInDB)
		{
			bool removeSuccessful = false;

			for (int i = list.Count - 1; i >= 0; i--) {
				int sceneIndex = scenesInDB.IndexOf(list[i]);
				if (sceneIndex == -1) {
					list.RemoveAt(i);
					removeSuccessful = true;
				}
			}

			return removeSuccessful;
		}

		private void OnDisable()
		{
			if (m_Initialized) {
				StorePinned();
				StoreScenes();
			}
		}

		//
		// Load save settings
		//
		private void InitializeData()
		{

			//
			// Cache available scenes
			//
			string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
			var scenesInDB = new List<string>(sceneGuids.Length);
			foreach (string guid in sceneGuids) {
				string scenePath = AssetDatabase.GUIDToAssetPath(guid);

				if (Array.FindIndex(excludeScenePaths, p => scenePath.IndexOf(p, StringComparison.OrdinalIgnoreCase) != -1) != -1)
					continue;

				scenesInDB.Add(scenePath);
			}

			m_Pinned = ReadListFromPrefs(PinnedScenesSetting);
			m_Scenes = ReadListFromPrefs(ScenesSetting);

			bool hasChanges = RemoveRedundant(m_Scenes, scenesInDB);
			hasChanges = RemoveRedundant(m_Pinned, m_Scenes) || hasChanges;

			foreach (string s in scenesInDB) {

				if (m_Scenes.IndexOf(s) == -1) {
					m_Scenes.Add(s);

					hasChanges = true;
				}
			}


			if (hasChanges) {
				StorePinned();
				StoreScenes();
			}
		}

		private void InitializeStyles()
		{
			LEFT_ALIGNED_BUTTON = new GUIStyle(GUI.skin.button);
			LEFT_ALIGNED_BUTTON.alignment = TextAnchor.MiddleLeft;
			LEFT_ALIGNED_BUTTON.padding.left = 10;
		}



		private void OnGUI()
		{

			// Initialize on demand (not on OnEnable), to make sure everything is up and running.
			if (!m_Initialized || AssetsChanged) {
				InitializeData();
				InitializeStyles();
				m_Initialized = true;
				AssetsChanged = false;
			}


			//
			// Draw Filter
			//
			EditorGUILayout.BeginHorizontal();
			GUILayout.Label("Search:", EditorStyles.boldLabel);


			GUI.SetNextControlName("FilterControl");
			m_Filter = EditorGUILayout.TextField(m_Filter, GUILayout.Height(20));

			// HACK: skip a frame to focus control, to avoid visual bugs. Bad bad Unity!
			if (m_FocusFilterField) {
				GUI.FocusControl("FilterControl");
				m_FocusFilterField = false;
			}

			// Clear on ESC
			if (Event.current.isKey && Event.current.keyCode == KeyCode.Escape && GUI.GetNameOfFocusedControl() == "FilterControl") {
				m_Filter = "";
				GUI.FocusControl("");
				Event.current.Use();
			}

			// Clear on button
			if (GUILayout.Button("X", GUILayout.Width(20.0f))) {
				m_Filter = "";
				GUI.FocusControl("");
				m_FocusFilterField = true;
				Repaint();
			}

			// Unfocus on enter. Open first scene from results.
			bool openFirstResult = false;
			if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "FilterControl") {
				GUI.FocusControl("");
				Repaint();
				Event.current.Use();

				if (!string.IsNullOrEmpty(m_Filter)) {
					openFirstResult = true;
				}

			}

			EditorGUILayout.EndHorizontal();


			var filterWords = string.IsNullOrEmpty(m_Filter) ? null : m_Filter.Split(FILTER_WORD_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
			if (openFirstResult) {
				m_Filter = "";
			}

			//
			// Show pinned scenes
			//
			if (m_Pinned.Count > 0) {
				GUILayout.Label("Pinned:", EditorStyles.boldLabel);

				float scrollViewHeight;
				var shouldScroll = ShouldScrollPinned(filterWords, out scrollViewHeight);

				if (shouldScroll) {
					EditorGUILayout.BeginVertical();
					m_ScrollPosPinned = EditorGUILayout.BeginScrollView(m_ScrollPosPinned, false, false, GUILayout.Height(scrollViewHeight));
				}

				bool hasChanges = RemoveRedundant(m_Pinned, m_Scenes);
				if (hasChanges) {
					StorePinned();
				}

				for (int i = 0; i < m_Pinned.Count; ++i) {
					var scenePath = m_Pinned[i];
					var sceneName = Path.GetFileNameWithoutExtension(scenePath);
					if (!IsFilteredOut(sceneName, filterWords)) {
						DrawSceneButtons(scenePath, sceneName, true, false);
					}
				}

				if (shouldScroll) {
					EditorGUILayout.EndScrollView();
					EditorGUILayout.EndVertical();
				}
			}




			//
			// Show all scenes
			//
			GUILayout.Label("Scenes:", EditorStyles.boldLabel);

			EditorGUILayout.BeginVertical();
			m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos, false, false);

			var filteredCount = 0;
			for (var i = 0; i < m_Scenes.Count; i++) {
				var scenePath = m_Scenes[i];
				var sceneName = Path.GetFileNameWithoutExtension(scenePath);

				// Do filtering
				if (IsFilteredOut(sceneName, filterWords))
					continue;

				filteredCount++;

				if (!m_Pinned.Contains(scenePath)) {
					DrawSceneButtons(scenePath, sceneName, false, openFirstResult);
					openFirstResult = false;
				}

				if (!m_ShowFullBigList && filteredCount >= SANITY_LIST_COUNT)
					break;
			}


			// Big lists support
			if (!m_ShowFullBigList && filteredCount >= SANITY_LIST_COUNT && GUILayout.Button("... Show All ...", GUILayout.ExpandWidth(true))) {
				m_ShowFullBigList = true;
				GUIUtility.ExitGUI();
			}

			if (m_ShowFullBigList && filteredCount >= SANITY_LIST_COUNT && GUILayout.Button("... Hide Some ...", GUILayout.ExpandWidth(true))) {
				m_ShowFullBigList = false;
				GUIUtility.ExitGUI();
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private bool ShouldScrollPinned(string[] filterWords, out float scrollViewHeight)
		{
			// Calculate pinned scroll view layout.
			const float LINE_PADDING = 6;
			float LINE_HEIGHT = EditorGUIUtility.singleLineHeight + LINE_PADDING;

			var scenesCount = (filterWords == null)
				? m_Pinned.Count
				: m_Pinned.Count(scenePath => !IsFilteredOut(Path.GetFileNameWithoutExtension(scenePath), filterWords));

			var pinnedTop = LINE_HEIGHT * 2 + 4; // Stuff before the pinned list (roughly).
			var pinnedTotalHeight = LINE_HEIGHT * scenesCount;

			scrollViewHeight = Mathf.Max(position.height * 0.6f - pinnedTop, LINE_HEIGHT * 3);
			return pinnedTotalHeight >= scrollViewHeight + LINE_PADDING;
		}

		private bool IsFilteredOut(string sceneName, string[] filterWords)
		{

			if (filterWords == null)
				return false;

			foreach (var filterWord in filterWords) {
				if (sceneName.IndexOf(filterWord, StringComparison.OrdinalIgnoreCase) == -1) {
					return true;
				}
			}

			return false;
		}

		private void MoveSceneAtTopOfList(string scenePath)
		{
			int idx = m_Scenes.IndexOf(scenePath);
			if (idx >= 0) {
				m_Scenes.RemoveAt(idx);
				m_Scenes.Insert(0, scenePath);
			}
		}

		private void DrawSceneButtons(string scenePath, string sceneName, bool isPinned, bool forceOpen)
		{

			EditorGUILayout.BeginHorizontal();

			m_SceneButtonTextCache.text = sceneName;
			m_SceneButtonTextCache.tooltip = scenePath;

			var scene = SceneManager.GetSceneByPath(scenePath);
			bool isSceneLoaded = scene.IsValid();
			bool isActiveScene = isSceneLoaded && scene == SceneManager.GetActiveScene();
			var loadedButton = isSceneLoaded ? (isActiveScene ? m_ActiveSceneButtonTextCache : m_RemoveSceneButtonTextCache) : m_AddSceneButtonTextCache;

			bool optionsPressed = GUILayout.Button(isPinned ? "O" : "@", GUILayout.Width(22));
			bool scenePressed = GUILayout.Button(m_SceneButtonTextCache, LEFT_ALIGNED_BUTTON) || forceOpen;
			bool loadPressed = GUILayout.Button(loadedButton, GUILayout.Width(20));


			if (scenePressed || optionsPressed || loadPressed) {
				// If scene was removed outside of Unity, the AssetModificationProcessor would not get notified.
				if (!File.Exists(scenePath)) {
					AssetsChanged = true;
					return;
				}
			}

			if (scenePressed) {


				if (Event.current.shift) {
					EditorUtility.RevealInFinder(scenePath);

				} else if (Application.isPlaying || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {

					if (Application.isPlaying) {
						// Note: to do this, the scene must me added to the build settings list.
						// Note2: Sometimes there are side effects with the lighting.
						SceneManager.LoadSceneAsync(scenePath);
					} else {
						EditorSceneManager.OpenScene(scenePath);
					}
					MoveSceneAtTopOfList(scenePath);
					//m_Filter = "";	// It's a feature. Sometimes you need to press on multiple scenes in a row.
					GUI.FocusControl("");
				}
			}


			if (optionsPressed) {
				if (isPinned) {
					ShowPinnedOptions(scenePath);
				} else {
					m_Pinned.Add(scenePath);
					StorePinned();
					StoreScenes();
				}
			}

			if (loadPressed) {
				if (Application.isPlaying) {
					if (!isSceneLoaded) {
						// Note: to do this, the scene must me added to the build settings list.
						SceneManager.LoadScene(scenePath, LoadSceneMode.Additive);
					} else if (!isActiveScene) {
						SceneManager.UnloadSceneAsync(scene);
					}
				} else {
					if (!isSceneLoaded) {
						EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
						MoveSceneAtTopOfList(scenePath);
					} else if (!isActiveScene) {
						EditorSceneManager.CloseScene(scene, true);
					}
				}
			}

			EditorGUILayout.EndHorizontal();
		}

		// Show context menu with options.
		private void ShowPinnedOptions(string scenePath)
		{
			var menu = new GenericMenu();
			int index = m_Pinned.IndexOf(scenePath);

			foreach (PinnedOptions value in Enum.GetValues(typeof(PinnedOptions))) {
				menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(value.ToString())), false, OnSelectPinnedOption, new KeyValuePair<PinnedOptions, int>(value, index));
			}

			menu.ShowAsContext();
		}

		private void OnSelectPinnedOption(object data)
		{
			var pair = (KeyValuePair<PinnedOptions, int>)data;
			int index = pair.Value;
			string temp;

			switch (pair.Key) {

				case PinnedOptions.Unpin:
					m_Pinned.RemoveAt(index);
					break;

				case PinnedOptions.MoveUp:
					if (index == 0)
						return;

					temp = m_Pinned[index];
					m_Pinned[index] = m_Pinned[index - 1];
					m_Pinned[index - 1] = temp;
					break;

				case PinnedOptions.MoveDown:
					if (index == m_Pinned.Count - 1)
						return;

					temp = m_Pinned[index];
					m_Pinned[index] = m_Pinned[index + 1];
					m_Pinned[index + 1] = temp;
					break;

				case PinnedOptions.MoveFirst:
					m_Pinned.Insert(0, m_Pinned[index]);
					m_Pinned.RemoveAt(index + 1);
					break;

				case PinnedOptions.MoveLast:
					m_Pinned.Add(m_Pinned[index]);
					m_Pinned.RemoveAt(index);
					break;

				case PinnedOptions.ShowInExplorer:
					EditorUtility.RevealInFinder(m_Pinned[index]);
					break;

				case PinnedOptions.ShowInProject:
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(m_Pinned[index]));
					break;
			}
			StorePinned();
			StoreScenes();
			Repaint();
		}
	}

	//
	// Monitors scene assets for any modifications and signals to the ScenesViewWindow.
	// 
	internal class ScenesInProjectChangeProcessor : UnityEditor.AssetModificationProcessor
	{

		public static void OnWillCreateAsset(string path)
		{
			if (path.EndsWith(".unity.meta") || path.EndsWith(".unity"))
				ScenesInProject.AssetsChanged = true;
		}

		public static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			if (path.EndsWith(".unity.meta") || path.EndsWith(".unity"))
				ScenesInProject.AssetsChanged = true;

			return AssetDeleteResult.DidNotDelete;
		}

		public static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
		{
			if (oldPath.EndsWith(".unity.meta") || oldPath.EndsWith(".unity"))
				ScenesInProject.AssetsChanged = true;

			return AssetMoveResult.DidNotMove;
		}
	}

}
