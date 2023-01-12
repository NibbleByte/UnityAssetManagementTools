using System.IO;
using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Text;

namespace DevLocker.Tools.AssetManagement
{
	/// <summary>
	/// Window with a list of all available scenes in the project:
	/// + quick access to scenes
	/// + easily load scenes additively
	/// + pin favourites
	/// + colorize scenes based on file name or path
	///
	/// Find it at "Tools / Asset Management / Scenes In Project" menu.
	///
	/// Initial version of the script: http://wiki.unity3d.com/index.php/SceneViewWindow by Kevin Tarchenski.
	/// Advanced (this) version by Filip Slavov (a.k.a. NibbleByte) - NibbleByte3@gmail.com.
	/// </summary>
	public class ScenesInProject : EditorWindow
	{
		#region Types definitions

		private enum SceneOptions
		{
			Pin,
			CopyAssetPath,
			Colorize,
			Exclude,
			MoveFirst,
			MoveLast,
			ShowInExplorer,
			ShowInProject,
		}

		private enum ColorizeOptions
		{
			Red,
			Green,
			Blue,
			Orange,
			Yellow,
			Brown,
			Purple,
			Clear,
			Custom,
		}

		private enum SortType
		{
			MostRecent,
			ByFileName,
			ByPath,
			DontSort = 20,
		}

		private enum QuickSortType
		{
			ByPath,
			ByFileName,
			ByFileSize,
			ByDateModified,
		}

		private enum SceneDisplay
		{
			SceneNames,
			SceneNamesWithParents,
			SceneFullPathsOmitFolders
		}

		private enum PreferencesTab
		{
			Personal = 0,
			Project = 1,
			About = 2,
		}

		private enum PackedSceneLoadedState
		{
			Unloaded = 0,
			Loaded = 1,
			MainScene = 2,
		}

		[Serializable]
		private struct PackedSceneState
		{
			public string Guid;
			public string Path;
			public PackedSceneLoadedState LoadedState;
			public bool IsLoaded => LoadedState != PackedSceneLoadedState.Unloaded;
			public bool IsMainScene => LoadedState == PackedSceneLoadedState.MainScene;
		}

		[Serializable]
		private struct PackedScenesPreference
		{
			public string Identifier;
			public List<PackedSceneState> PackedStates;

			public PackedScenesPreference Clone()
			{
				var clone = (PackedScenesPreference) MemberwiseClone();
				clone.PackedStates = PackedStates.ToList();

				return clone;
			}
		}

		[Serializable]
		private class SceneEntry
		{
			public SceneEntry() { }
			public SceneEntry(string guid, string path)
			{
				Path = path;
				Guid = guid;
				RefreshDetails();
			}
			public SceneEntry(string guid, IEnumerable<PackedSceneState> packedScenes)
			{
				Guid = guid;
				PackedSceneState mainScene = packedScenes.First(s => s.IsMainScene);
				Path = mainScene.Path;
				PackedScenes = packedScenes.ToArray();
				RefreshDetails();
			}

			public string Path;
			public string Guid;
			public string Name;
			public string Folder;
			public string DisplayName;
			public bool FirstInGroup = false;

			public ColorizePattern ColorizePattern;

			public bool IsPack => PackedScenes.Length > 0;

			public PackedSceneState[] PackedScenes = new PackedSceneState[0];

			public PackedSceneState PackedMainSceneState => PackedScenes.FirstOrDefault(s => s.IsMainScene);

			public const string PackedSceneGuidPrefix = "pack-";

			public void RefreshDetails()
			{
				Name = string.IsNullOrEmpty(Path) ? "" : System.IO.Path.GetFileNameWithoutExtension(Path);
				Folder = string.IsNullOrEmpty(Path) ? "" : Path.Substring(0, Path.LastIndexOf('/'));
			}

			public SceneEntry Clone()
			{
				var clone = (SceneEntry) MemberwiseClone();
				clone.ColorizePattern = ColorizePattern?.Clone();
				clone.PackedScenes = PackedScenes.ToArray();
				return clone;
			}

			public override string ToString()
			{
				return IsPack ? "Pack: " + Path : Path;
			}

			private readonly char[] m_SerializeMainSeparator = new char[] { '|' };
			private readonly char[] m_SerializePackedInputsSeparator = new char[] { ';' };
			private readonly char[] m_SerializePackedParamsSeparator = new char[] { ' ' };

			public string Serialize()
			{
				var data = new StringBuilder();
				data.Append(Path);
				data.Append(m_SerializeMainSeparator[0]);
				data.Append(Guid);

				if (IsPack) {
					data.Append(m_SerializeMainSeparator[0]);
					foreach(PackedSceneState packedScene in PackedScenes) {
						data.Append(packedScene.Guid);
						data.Append(m_SerializePackedParamsSeparator[0]);
						data.Append((int)packedScene.LoadedState);
						data.Append(m_SerializePackedInputsSeparator[0]);
					}
				}

				return data.ToString();
			}

			public void Deserialize(string data)
			{
				var inputs = data.Split(m_SerializeMainSeparator, StringSplitOptions.RemoveEmptyEntries);
				Path = inputs.FirstOrDefault() ?? "";
				Guid = string.Empty;

				// LEGACY: initially only paths were stored, no guids.
				if (inputs.Length > 1) {
					Guid = inputs[1];

					if (inputs.Length > 2) {
						var packedInputs = inputs[2].Split(m_SerializePackedInputsSeparator, StringSplitOptions.RemoveEmptyEntries);
						var packedScenes = new List<PackedSceneState>(packedInputs.Length);

						foreach(string packedInput in packedInputs) {
							var packedParams = packedInput.Split(m_SerializePackedParamsSeparator, StringSplitOptions.RemoveEmptyEntries);
							if (packedParams.Length == 2) {
								int loadedParam;
								int.TryParse(packedParams[1], out loadedParam);

								var packedScene = new PackedSceneState() {
									Guid = packedParams[0],
									Path = AssetDatabase.GUIDToAssetPath(packedParams[0]),
									LoadedState = (PackedSceneLoadedState) loadedParam,
								};

								packedScenes.Add(packedScene);

								if (packedScene.IsMainScene && !string.IsNullOrEmpty(packedScene.Path)) {
									Path = packedScene.Path;
								}
							}
						}

						PackedScenes = packedScenes.ToArray();
					}
				}

				RefreshDetails();
			}
		}

		[Serializable]
		private class ColorizePattern
		{
			[Tooltip("Relative path (contains '/') or name match.\nCan have multiple patterns separated by ';'.\nAppend '|' then guid to track the assets when renamed.")]
			public string Patterns = string.Empty;
			public Color BackgroundColor = Color.black;
			public Color TextColor = Color.white;

			public ColorizePattern Clone()
			{
				return (ColorizePattern) MemberwiseClone();
			}
		}

		[Serializable]
		private class PersonalPreferences
		{
			public SortType SortType = SortType.MostRecent;
			public SceneDisplay SceneDisplay = SceneDisplay.SceneNames;
			public int DisplayParentsCount = 1;
			public List<string> DisplayRemoveFolders = new List<string>() { "Assets" };
			public int ListCountLimit = 64;     // Round number... :D
			public int SpaceBetweenGroups = 6;  // Pixels between scenes with different folders.
			public int SpaceBetweenGroupsPinned = 0;  // Same but for pinned.

			public List<string> Exclude = new List<string>();   // Exclude paths OR filenames (per project preference)

			public List<ColorizePattern> ColorizePatterns = new List<ColorizePattern>();

			public float SplitterY = -1;			// Hidden preference.

			public PersonalPreferences Clone()
			{
				var clone = (PersonalPreferences)MemberwiseClone();
				clone.DisplayRemoveFolders = new List<string>(DisplayRemoveFolders);
				clone.Exclude = new List<string>(Exclude);
				clone.ColorizePatterns = new List<ColorizePattern>(ColorizePatterns);

				return clone;
			}
		}

		[Serializable]
		private class ProjectPreferences
		{
			public List<ColorizePattern> ColorizePatterns = new List<ColorizePattern>();
			public List<PackedScenesPreference> ScenePacks = new List<PackedScenesPreference>();
			public List<string> Exclude = new List<string>();   // Exclude paths OR filenames (per project preference)

			public ProjectPreferences Clone()
			{
				var clone = (ProjectPreferences)MemberwiseClone();

				clone.ColorizePatterns = new List<ColorizePattern>(this.ColorizePatterns);
				clone.ScenePacks = this.ScenePacks.Select(sp => sp.Clone()).ToList();
				clone.Exclude = new List<string>(this.Exclude);

				return clone;
			}
		}

		#endregion

		private readonly char[] FilterWordsSeparator = new char[] { ' ', '\t' };

		private GUIStyle SearchLabelStyle;
		private GUIContent SearchLabelContent = new GUIContent("Search:", "Filter out scenes by name.\nPress Enter to open first unpinned result.");

		private GUIStyle SearchFieldStyle;
		private GUIStyle SearchFieldCancelStyle;
		private GUIStyle SearchFieldCancelEmptyStyle;

		private GUIStyle SceneButtonStyle;
		private GUIContent SceneButtonContentCache = new GUIContent();
		private GUIStyle SceneOptionsButtonStyle;
		private GUIContent SceneOptionsButtonContent = new GUIContent("\u2261", "Options...");	// \u2261 \u20AA

		private GUIStyle SceneOptionsPackButtonStyle;
		private GUIContent SceneOptionsPackButtonContent = new GUIContent("\u205c", "Pack options...");

		private GUIContent ScenePlayButtonContent = new GUIContent("\u25BA", "Play directly");
		private GUIStyle ScenePlayButtonStyle;

		private GUIStyle SceneLoadedButtonStyle;
		private GUIContent SceneLoadedButtonAddContent = new GUIContent("+", "Load scene additively");
		private GUIContent SceneLoadedButtonActiveContent = new GUIContent("*", "Active scene (cannot unload)");
		private GUIContent SceneLoadedButtonRemoveContent = new GUIContent("-", "Unload scene");

		private float SceneButtonHeight;

		private GUIStyle SplitterStyle;
		private GUIStyle DragHandlerStyle;
		private GUIStyle FoldOutBoldStyle;

		private GUIStyle ToolbarButtonStyle;
		private GUIContent PreferencesButtonContent;
		private GUIContent QuickSortButtonContent;
		private GUIContent ReloadButtonContent;
		private GUIStyle CreatePackageButtonStyle;
		private GUIContent CreatePackageButtonContent = new GUIContent("\u205c", "Create a pack from the currently loaded scenes.\nPacks bundle scenes to be loaded together.");

		private IEnumerable<GUIStyle> m_SceneButtonStyles {
			get {
				yield return SceneButtonStyle;
				yield return SceneOptionsButtonStyle;
				yield return SceneOptionsPackButtonStyle;
				yield return ScenePlayButtonStyle;
				yield return SceneLoadedButtonStyle;
			}
		}

		internal static bool AssetsChanged = false;

		// Used to synchronize instances.
		private static List<ScenesInProject> m_Instances = new List<ScenesInProject>();


		private bool m_Initialized = false;

		private bool m_LaunchedSceneDirectly = false;
		private SceneAsset m_PreviousStartScene;

		// In big projects with 1k number of scenes, don't show everything.
		[NonSerialized]
		private bool m_ShowFullBigList = false;

		private Vector2 m_ScrollPos;
		private Vector2 m_ScrollPosPinned;
		private Rect m_SplitterRect = new Rect(-1, -1, -1, -1);
		private bool m_IsSplitterDragged;

		private string m_Filter = string.Empty;
		private bool m_FocusFilterField = false; // HACK!

		[SerializeField]
		private PersonalPreferences m_PersonalPrefs = new PersonalPreferences();

		[SerializeField]
		private ProjectPreferences m_ProjectPrefs = new ProjectPreferences();

		private List<SceneEntry> m_Scenes = new List<SceneEntry>();
		private List<SceneEntry> m_Pinned = new List<SceneEntry>();     // NOTE: m_Scenes & m_Pinned are not duplicated
		private int m_PinnedGroupsCount = 0;

		[NonSerialized]
		private SceneEntry m_DraggedEntity;

		private SerializedObject m_SerializedObject;

		private bool m_ShowPreferences = false;


		// Legacy preferences path from before Unity "UserSettings" folder existed
		private const string Legacy_PersonalPreferencesPath = "Library/ScenesInProject.prefs";
		private const string Legacy_SettingsPathScenes = "Library/ScenesInProject.Scenes.txt";
		private const string Legacy_SettingsPathPinnedScenes = "Library/ScenesInProject.PinnedScenes.txt";

#if UNITY_2020_1_OR_NEWER
		private const string PersonalPreferencesPath = "UserSettings/ScenesInProject.prefs";
		private const string SettingsPathScenes = "UserSettings/ScenesInProject.Scenes.txt";
		private const string SettingsPathPinnedScenes = "UserSettings/ScenesInProject.PinnedScenes.txt";
#else
		private const string PersonalPreferencesPath = Legacy_PersonalPreferencesPath;
		private const string SettingsPathScenes = Legacy_SettingsPathScenes;
		private const string SettingsPathPinnedScenes = Legacy_SettingsPathPinnedScenes;
#endif

		private const string ProjectPreferencesPath = "ProjectSettings/ScenesInProject.prefs";


		[MenuItem("Tools/Asset Management/Scenes In Project", false, 68)]
		private static void Init()
		{
			var window = (ScenesInProject)GetWindow(typeof(ScenesInProject), false, "Scenes In Project");
			if (!window.m_Initialized) {
				window.position = new Rect(window.position.xMin + 100f, window.position.yMin + 100f, 400f, 600f);
				window.minSize = new Vector2(300f, 200f);
			}
		}

#if UNITY_2019_2_OR_NEWER
		/// <summary>
		/// Called when assembly reload is disabled.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void InitOnPlay()
		{
			// NOTE: This executes after OnEnable() when doing assembly reload so it may be doing double the work,
			//		 but since the documentation is not very clear about it, I'm not taking any chances. Also it is not supported prior to Unity 2019.
			foreach (var instance in m_Instances) {
				if (instance.ClearDirectPlayScene())
					break;
			};
		}
#endif

		// Hidden Unity function, used to draw lock and other buttons at the top of the window.
		private void ShowButton(Rect rect)
		{
			if (GUI.Button(rect, "+", GUI.skin.label)) {
				ScenesInProject window = CreateInstance<ScenesInProject>();
				window.titleContent = titleContent;
				window.Show();

				window.position = new Rect(window.position.xMin + 100f, window.position.yMin + 100f, 400f, 600f);
				window.minSize = new Vector2(300f, 200f);
			}
		}

#region LEGACY SUPPORT
		private bool LEGACY_ReadPinnedListFromPrefs()
		{
			bool hasChanged = false;
			var setting = string.Format("SceneView_PinnedScenes_{0}", System.Text.RegularExpressions.Regex.Replace(Application.dataPath, @"[/\\:?]", ""));

			string storage = EditorPrefs.GetString(setting, string.Empty);

			if (!string.IsNullOrEmpty(storage)) {
				var preferencesList = new List<string>(storage.Split(';'));
				foreach (var path in preferencesList) {
					var entry = m_Scenes.Find(e => e.Path == path);

					if (entry != null) {
						m_Scenes.Remove(entry);

						if (m_Pinned.Any(e => e.Path == path)) {
							Debug.LogError($"ScenesInProject: scene {path} was present in m_Scenes and m_Pinned");
							continue;
						}

						m_Pinned.Add(entry);
						hasChanged = true;

					} else if (!m_Pinned.Any(e => e.Path == path)) {
						string guid = AssetDatabase.AssetPathToGUID(path);
						if (!string.IsNullOrEmpty(guid)) {
							m_Pinned.Add(new SceneEntry(guid, path));
							hasChanged = true;
						}
					}
				}

				AutoSnapSplitter();
				EditorPrefs.DeleteKey(setting);
			}

			// Do Scenes as well.
			setting = string.Format("SceneView_Scenes_{0}", System.Text.RegularExpressions.Regex.Replace(Application.dataPath, @"[/\\:?]", ""));

			storage = EditorPrefs.GetString(setting, string.Empty);

			if (!string.IsNullOrEmpty(storage)) {
				var preferencesList = new List<string>(storage.Split(';'));
				m_Scenes.RemoveAll(e => preferencesList.Contains(e.Path));
				var toInsert = preferencesList
					.Where(p => m_Pinned.FindIndex(e => e.Path == p) == -1)
					.Select(p => new SceneEntry(AssetDatabase.GUIDToAssetPath(p), p))
					.Where(e => !string.IsNullOrEmpty(e.Guid));
				m_Scenes.InsertRange(0, toInsert);
				hasChanged = true;

				EditorPrefs.DeleteKey(setting);
			}

			return hasChanged;
		}
#endregion

		private void StoreAllScenes()
		{
			try {
				File.WriteAllLines(SettingsPathPinnedScenes, m_Pinned.Select(e => e.Serialize()));
				File.WriteAllLines(SettingsPathScenes, m_Scenes.Select(e => e.Serialize()));
			}
			catch (Exception ex) {
				Debug.LogException(ex);
			}
		}

		private void StorePersonalPrefs()
		{
			try {
				File.WriteAllText(PersonalPreferencesPath, JsonUtility.ToJson(m_PersonalPrefs, true));
			}
			catch (Exception ex) {
				Debug.LogException(ex);
				EditorUtility.DisplayDialog("Error", $"Failed to write file:\n\"{PersonalPreferencesPath}\"\n\nData not saved! Check the logs for more info.", "Ok");
			}
		}

		private void StoreProjectPrefs()
		{
			try {
				File.WriteAllText(ProjectPreferencesPath, JsonUtility.ToJson(m_ProjectPrefs, true));
			}
			catch (Exception ex) {
				Debug.LogException(ex);
				EditorUtility.DisplayDialog("Error", $"Failed to write file:\n\"{ProjectPreferencesPath}\"\n\nData not saved! Check the logs for more info.", "Ok");
			}
		}

		private bool UpdatePreferenceLists()
		{
			bool hasChanged = false;

			// Update paths from guids.
			for(int i = m_PersonalPrefs.ColorizePatterns.Count - 1; i >= 0; i--) {

				// NOTE: skipping ';' splits. Don't care enough about it...
				ColorizePattern colors = m_PersonalPrefs.ColorizePatterns[i];

				bool isPath = colors.Patterns.Contains("/");

				// Find best match for this scene entry.
				if (isPath) {

					int guidSeparatorIndex = colors.Patterns.LastIndexOf('|');
					if (guidSeparatorIndex != -1) {
						string patternPath = colors.Patterns.Substring(0, guidSeparatorIndex);
						string patternGuid = colors.Patterns.Substring(guidSeparatorIndex + 1);

						if (patternGuid.StartsWith(SceneEntry.PackedSceneGuidPrefix, StringComparison.OrdinalIgnoreCase)) {
							SceneEntry sceneEntry = m_Pinned.FirstOrDefault(s => s.Guid == patternGuid);
							if (sceneEntry == null) {
								m_PersonalPrefs.ColorizePatterns.RemoveAt(i);
								hasChanged = true;
							} else {
								PackedSceneState mainScene = sceneEntry.PackedMainSceneState;
								string foundPackedScenePath = AssetDatabase.GUIDToAssetPath(mainScene.Guid);

								if (string.IsNullOrEmpty(foundPackedScenePath)) {
									m_PersonalPrefs.ColorizePatterns.RemoveAt(i);
									hasChanged = true;
								} else if (foundPackedScenePath != patternPath) {
									colors.Patterns = colors.Patterns.Replace(patternPath, foundPackedScenePath);
									hasChanged = true;
								}
							}
							continue;
						}

						string foundPath = AssetDatabase.GUIDToAssetPath(patternGuid);

						if (string.IsNullOrEmpty(foundPath)) {
							m_PersonalPrefs.ColorizePatterns.RemoveAt(i);
							hasChanged = true;
							continue;
						}

						if (foundPath != patternPath) {
							colors.Patterns = colors.Patterns.Replace(patternPath, foundPath);
							hasChanged = true;
						}
					}
				}
			}

			// Update paths from guids.
			for(int i = m_PersonalPrefs.Exclude.Count - 1; i >= 0 ; i--) {
				string exclude = m_PersonalPrefs.Exclude[i];

				int guidSeparatorIndex = exclude.LastIndexOf('|');
				if (guidSeparatorIndex != -1) {
					string patternPath = exclude.Substring(0, guidSeparatorIndex);
					string patternGuid = exclude.Substring(guidSeparatorIndex + 1);
					string foundPath = AssetDatabase.GUIDToAssetPath(patternGuid);

					if (string.IsNullOrEmpty(foundPath)) {
						m_PersonalPrefs.Exclude.RemoveAt(i);
						hasChanged = true;
						continue;
					}

					if (foundPath != patternPath) {
						m_PersonalPrefs.Exclude[i] = exclude.Replace(patternPath, foundPath);
						hasChanged = true;
					}
				}
			}

			return hasChanged;
		}

		private bool UpdateScenesList(List<SceneEntry> list, HashSet<string> knownGuids)
		{
			bool hasChanged = false;

			for (int i = list.Count - 1; i >= 0; i--) {
				SceneEntry entry = list[i];

				// LEGACY: initially only paths were stored, no guids.
				if (string.IsNullOrEmpty(entry.Guid)) {
					entry.Guid = AssetDatabase.AssetPathToGUID(entry.Path);
					hasChanged = true;

				} else {
					// Update the path in case it changed.

					string foundPath = entry.IsPack ? "" : AssetDatabase.GUIDToAssetPath(entry.Guid);
					if (entry.IsPack) {

						for(int packedIndex = 0; packedIndex < entry.PackedScenes.Length; ++packedIndex) {
							PackedSceneState packedScene = entry.PackedScenes[packedIndex];
							string foundPackedScenePath = AssetDatabase.GUIDToAssetPath(packedScene.Guid);

							if (packedScene.Path != foundPackedScenePath) {
								packedScene.Path = foundPackedScenePath;
								entry.PackedScenes[packedIndex] = packedScene;
								hasChanged = true;
							}

							if (packedScene.IsMainScene) {
								foundPath = foundPackedScenePath;
							}
						}
					}

					// Because deleted assets keep their guid-to-path mapping until Unity is restarted.
					if (!string.IsNullOrEmpty(foundPath) && !File.Exists(foundPath)) {
						foundPath = "";
					}

					if (entry.Path != foundPath) {
						entry.Path = foundPath;
						entry.RefreshDetails();
						hasChanged = true;
					}
				}

				if (string.IsNullOrEmpty(entry.Guid) || string.IsNullOrEmpty(entry.Path)) {
					list.RemoveAt(i);
					hasChanged = true;
					continue;
				}

				if (!entry.IsPack && ShouldExclude(entry.Guid, entry.Path, m_ProjectPrefs.Exclude.Concat(m_PersonalPrefs.Exclude))) {
					list.RemoveAt(i);
					hasChanged = true;
					continue;
				}

				knownGuids.Add(entry.Guid);
			}

			return hasChanged;
		}

		private bool UpdatePackedScenesFromPreferences()
		{
			bool hasChanges = false;

			bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

			foreach (PackedScenesPreference packedPreference in m_ProjectPrefs.ScenePacks) {
				var guid = SceneEntry.PackedSceneGuidPrefix + packedPreference.Identifier;

				if (!m_Pinned.Any(s => s.Guid == guid)) {
					var entry = new SceneEntry(guid, packedPreference.PackedStates);
					m_Pinned.Insert(0, entry);
					hasChanges = true;
				}
			}

			if (hasChanges && shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			return hasChanges;
		}

		private static void SortScenes(List<SceneEntry> list, SortType sortType)
		{
			switch(sortType) {
				case SortType.MostRecent:
				case SortType.DontSort:
					break;

				case SortType.ByFileName:
					list.Sort((a, b) => Path.GetFileNameWithoutExtension(a.Path).CompareTo(Path.GetFileNameWithoutExtension(b.Path)));
					break;

				case SortType.ByPath:
					list.Sort((a, b) => a.Path.CompareTo(b.Path));
					break;

				default: throw new NotImplementedException();
			}
		}

		private int RegroupScenes(List<SceneEntry> list)
		{
			int groupsCount = 0;

			// Grouping scenes with little entries looks silly. Don't do grouping.
			if (list.Count < 6) {
				foreach(var sceneEntry in list) {
					sceneEntry.FirstInGroup = false;
				}

				return groupsCount;
			}

			// Consider the following example of grouping.
			// foo1    1
			// foo2    2
			// foo3    3
			//
			// bar1    1
			// bar2    2
			// bar3    3
			//
			// pepo    1
			// gogo    1
			// lili    1
			//
			// zzz1    1
			// zzz2    2
			// zzz3    3
			// zzz4    4
			//
			// Roro8   1


			list.First().FirstInGroup = false;
			int entriesInGroup = 1;
			string prevFolder, currFolder, nextFolder;


			for (int i = 1; i < list.Count - 1; ++i) {
				prevFolder = list[i - 1].Folder;
				currFolder = list[i].Folder;
				nextFolder = list[i + 1].Folder;

				list[i].FirstInGroup = false;

				if (prevFolder == currFolder) {
					entriesInGroup++;
					continue;
				}

				if (currFolder == nextFolder) {
					list[i].FirstInGroup = true;
					groupsCount++;
					entriesInGroup = 1;
					continue;
				}

				if (entriesInGroup > 1) {
					list[i].FirstInGroup = true;
					groupsCount++;
					entriesInGroup = 1;
					continue;
				}
			}

			// Do last element
			prevFolder = list[list.Count - 2].Folder;
			currFolder = list[list.Count - 1].Folder;

			bool lastStandAlone = entriesInGroup > 1 && prevFolder != currFolder;
			list.Last().FirstInGroup = lastStandAlone;	// Always set this to override old values!
			if (lastStandAlone) {
				groupsCount++;
			}

			return groupsCount;
		}

		private void RefreshDisplayNames(List<SceneEntry> list)
		{
			foreach(var sceneEntry in list) {

				if (sceneEntry.IsPack) {
					sceneEntry.DisplayName = $"[\"{sceneEntry.Name}\" + {sceneEntry.PackedScenes.Length - 1} scenes]";
					continue;
				}

				switch(m_PersonalPrefs.SceneDisplay) {

					case SceneDisplay.SceneNames:

						sceneEntry.DisplayName = sceneEntry.Name;
						break;

					case SceneDisplay.SceneNamesWithParents:

						int displayNameStartIndex = sceneEntry.Path.LastIndexOf('/');
						for(int i = 0; i < m_PersonalPrefs.DisplayParentsCount; ++i) {
							displayNameStartIndex = sceneEntry.Path.LastIndexOf('/', displayNameStartIndex - 1);
							if (displayNameStartIndex == -1) {
								break;
							}
						}

						sceneEntry.DisplayName = sceneEntry.Path.Substring(displayNameStartIndex + 1);
						sceneEntry.DisplayName = sceneEntry.DisplayName.Remove(sceneEntry.DisplayName.LastIndexOf('.'));

						//sceneEntry.DisplayName = sceneEntry.DisplayName.Insert(sceneEntry.DisplayName.LastIndexOf('/') + 1, " ");

						break;

					case SceneDisplay.SceneFullPathsOmitFolders:

						IEnumerable<string> pathFolders = sceneEntry.Folder.Split('/');
						pathFolders = pathFolders.Where(f => !m_PersonalPrefs.DisplayRemoveFolders.Contains(f, StringComparer.OrdinalIgnoreCase));

						sceneEntry.DisplayName = string.Join("/", pathFolders);
						sceneEntry.DisplayName += "/" + sceneEntry.Name;	// Skip extension.
						break;
				}
			}
		}

		private void RefreshColorizePatterns(List<SceneEntry> list)
		{
			var splitters = new char[] { ';' };

			foreach(var sceneEntry in list) {
				sceneEntry.ColorizePattern = GetMatchedColorPattern(sceneEntry, m_ProjectPrefs.ColorizePatterns.Concat(m_PersonalPrefs.ColorizePatterns));
			}
		}

		private static ColorizePattern GetMatchedColorPattern(SceneEntry sceneEntry, IEnumerable<ColorizePattern> colorPatterns)
		{
			var splitters = new char[] { ';' };

			bool matchedByName = false;

			ColorizePattern colorPattern = null;

			foreach (ColorizePattern colors in colorPatterns) {

				var patterns = colors.Patterns.Split(splitters, StringSplitOptions.RemoveEmptyEntries);

				foreach (string pattern in patterns) {

					bool isPath = pattern.Contains("/");

					// Find best match for this scene entry.
					if (isPath) {

						int guidSeparatorIndex = pattern.LastIndexOf('|');
						if (guidSeparatorIndex != -1) {
							string patternGuid = pattern.Substring(guidSeparatorIndex + 1);
							if (sceneEntry.Guid == patternGuid) {
								// This is always preferred to path or name.
								return colors;
							}

							// Packs match only by guid and don't mix with other scenes.
							if (sceneEntry.IsPack || patternGuid.StartsWith(SceneEntry.PackedSceneGuidPrefix, StringComparison.OrdinalIgnoreCase))
								continue;

						} else {
							guidSeparatorIndex = pattern.Length;
						}

						// If this is colorized dir, match without the guid part.
						if (!matchedByName && sceneEntry.Path.StartsWith(pattern.Substring(0, guidSeparatorIndex), StringComparison.OrdinalIgnoreCase)) {

							var prevPattern = colorPattern?.Patterns ?? string.Empty;

							// Allow only better path match to override previous - only paths, no names.
							// Note: This doesn't work for multiple patterns.
							var betterPath = prevPattern.Contains("/") && prevPattern.Length <= pattern.Length;

							if (colorPattern == null || betterPath) {
								colorPattern = colors;
							}
						}

					} else {

						// This is preferred to path, but not to guid.
						if (!matchedByName && sceneEntry.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) != -1) {
							colorPattern = colors;
							matchedByName = true;
						}
					}
				}
			}

			return colorPattern;
		}

		private void OnEnable()
		{
			ClearDirectPlayScene();

			m_Instances.Add(this);

			m_SerializedObject = new SerializedObject(this);
		}

		private void OnDisable()
		{
			m_Instances.Remove(this);

			// NOTE: OnDisable() gets called when turning on "Maximize" on the GameView.
			//		 If "Play Maximized" is on, OnDisables() get called before InitOnPlay() when assembly reload is off.
			ClearDirectPlayScene();

			if (m_Initialized) {
				StoreAllScenes();
			}

			if (m_SerializedObject != null) {
				m_SerializedObject.Dispose();
			}
		}

		//
		// Load save settings
		//
		private void LoadData()
		{
			if (File.Exists(PersonalPreferencesPath)) {
				m_PersonalPrefs = JsonUtility.FromJson<PersonalPreferences>(File.ReadAllText(PersonalPreferencesPath));
			} else if (File.Exists(Legacy_PersonalPreferencesPath)) {
				m_PersonalPrefs = JsonUtility.FromJson<PersonalPreferences>(File.ReadAllText(Legacy_PersonalPreferencesPath));
			} else {
				m_PersonalPrefs = new PersonalPreferences();
			}

			if (File.Exists(ProjectPreferencesPath)) {
				m_ProjectPrefs = JsonUtility.FromJson<ProjectPreferences>(File.ReadAllText(ProjectPreferencesPath));
			} else {
				m_ProjectPrefs = new ProjectPreferences();
			}

			Func<string, List<SceneEntry>> ParseScenesFromFile = (filePath) => {
				var list = new List<SceneEntry>();

				string[] lines = File.ReadAllLines(filePath);
				foreach(string line in lines) {
					var entry = new SceneEntry();
					entry.Deserialize(line);
					list.Add(entry);
				}

				return list;
			};

			if (File.Exists(SettingsPathPinnedScenes)) {
				m_Pinned = ParseScenesFromFile(SettingsPathPinnedScenes);
			} else if (File.Exists(Legacy_SettingsPathPinnedScenes)) {
				m_Pinned = ParseScenesFromFile(Legacy_SettingsPathPinnedScenes);
			}

			if (File.Exists(SettingsPathScenes)) {
				m_Scenes = ParseScenesFromFile(SettingsPathScenes);
			} else if (File.Exists(Legacy_SettingsPathScenes)) {
				m_Scenes = ParseScenesFromFile(Legacy_SettingsPathScenes);
			}
		}

		private void InitializeData()
		{
			bool shouldAutoSnapSplitter = false;
			if (!m_Initialized) {
				LoadData();

				shouldAutoSnapSplitter = InitializeSplitter(m_PersonalPrefs.SplitterY);
			}

			if (UpdatePreferenceLists()) {
				StorePersonalPrefs();
			}
			bool hasChanges = false;

			hasChanges = UpdatePackedScenesFromPreferences() || hasChanges;

			var knownGuids = new HashSet<string>();
			hasChanges = UpdateScenesList(m_Scenes, knownGuids) || hasChanges;
			hasChanges = UpdateScenesList(m_Pinned, knownGuids) || hasChanges;

			//
			// Cache available scenes
			//
			string[] foundSceneGuids = AssetDatabase.FindAssets("t:Scene");
			foreach (string guid in foundSceneGuids) {

				if (knownGuids.Contains(guid))
					continue;

				string scenePath = AssetDatabase.GUIDToAssetPath(guid);

				if (ShouldExclude(guid, scenePath, m_ProjectPrefs.Exclude.Concat(m_PersonalPrefs.Exclude)))
					continue;

				m_Scenes.Add(new SceneEntry(guid, scenePath));
				hasChanges = true;
			}

			if (!m_Initialized && LEGACY_ReadPinnedListFromPrefs()) {
				hasChanges = true;
			}

			if (hasChanges || !m_Initialized) {
				SortScenes(m_Scenes, m_PersonalPrefs.SortType);

				RefreshDisplayNames(m_Scenes);
				RefreshDisplayNames(m_Pinned);

				RefreshColorizePatterns(m_Scenes);
				RefreshColorizePatterns(m_Pinned);

				StoreAllScenes();
			}

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);
		}

		private void InitializeStyles()
		{
			titleContent.image = EditorGUIUtility.FindTexture("Favorite");

			SceneButtonStyle = new GUIStyle(GUI.skin.button);
			SceneButtonStyle.alignment = TextAnchor.MiddleLeft;
			SceneButtonStyle.padding.left = 10;

			SceneButtonHeight = EditorGUIUtility.singleLineHeight + SceneButtonStyle.margin.top + SceneButtonStyle.margin.bottom;

			SceneOptionsButtonStyle = new GUIStyle(GUI.skin.button);
			SceneOptionsButtonStyle.alignment = TextAnchor.MiddleCenter;
			SceneOptionsButtonStyle.padding.left += 2;
#if UNITY_2019_1_OR_NEWER
			SceneOptionsButtonStyle.padding.top = 1;
			SceneOptionsButtonStyle.padding.bottom = 1;
			SceneOptionsButtonStyle.fontSize = 14;	// Unity 2020+ default is 12 and squishes the character.
#endif

			SceneOptionsPackButtonStyle = new GUIStyle(GUI.skin.button);
			SceneOptionsPackButtonStyle.alignment = TextAnchor.MiddleCenter;
#if UNITY_2019_1_OR_NEWER
			SceneOptionsPackButtonStyle.padding = new RectOffset(0, 0, -4, 0);
			SceneOptionsPackButtonStyle.fontSize = 19;
#else
			SceneOptionsPackButtonStyle.padding = new RectOffset(2, 0, -2, 0);
			SceneOptionsPackButtonStyle.fontSize = 17;
#endif

			CreatePackageButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
			CreatePackageButtonStyle.alignment = TextAnchor.MiddleCenter;
#if UNITY_2019_1_OR_NEWER
			CreatePackageButtonStyle.padding = new RectOffset(3, 0, -4, 1);
			CreatePackageButtonStyle.fontSize = 20;
#else
			CreatePackageButtonStyle.padding = new RectOffset(4, 0, -2, 0);
			CreatePackageButtonStyle.fontSize = 17;
#endif

			ScenePlayButtonStyle = new GUIStyle(GUI.skin.GetStyle("ButtonLeft"));
			ScenePlayButtonStyle.alignment = TextAnchor.MiddleCenter;
			ScenePlayButtonStyle.padding.left += 2;
			ScenePlayButtonStyle.padding.right -= 2;

			SceneLoadedButtonStyle = new GUIStyle(GUI.skin.GetStyle("ButtonRight"));
			SceneLoadedButtonStyle.alignment = TextAnchor.MiddleCenter;
			SceneLoadedButtonStyle.padding.left = SceneLoadedButtonStyle.padding.right = 2;
			SceneLoadedButtonStyle.contentOffset = new Vector2(1f, 0f);

			SplitterStyle = new GUIStyle(GUI.skin.box);
			SplitterStyle.alignment = TextAnchor.MiddleCenter;
			SplitterStyle.clipping = TextClipping.Overflow;
			SplitterStyle.contentOffset = new Vector2(0f, -1f);

			DragHandlerStyle = new GUIStyle(GUI.skin.GetStyle("RL DragHandle"));
			//DRAGHANDLER_STYLE.contentOffset = new Vector2(0f, Mathf.FloorToInt(EditorGUIUtility.singleLineHeight / 2f) + 2);

			FoldOutBoldStyle = new GUIStyle(EditorStyles.foldout);
			FoldOutBoldStyle.fontStyle = FontStyle.Bold;

			SearchLabelStyle = new GUIStyle(EditorStyles.boldLabel);
			SearchLabelStyle.margin.top = 1;

			SearchFieldStyle = GUI.skin.GetStyle("ToolbarSeachTextField");
			SearchFieldCancelStyle = GUI.skin.GetStyle("ToolbarSeachCancelButton");
			SearchFieldCancelEmptyStyle = GUI.skin.GetStyle("ToolbarSeachCancelButtonEmpty");

			ToolbarButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
			ToolbarButtonStyle.padding = new RectOffset();

			PreferencesButtonContent = new GUIContent(EditorGUIUtility.FindTexture("Settings"), "Preferences...");
			QuickSortButtonContent = new GUIContent(EditorGUIUtility.FindTexture("CustomSorting"), "Quick sort scenes");
			ReloadButtonContent = new GUIContent(EditorGUIUtility.FindTexture("Refresh"), "Reload currently loaded scenes");
		}

		private void SynchronizeInstancesToMe()
		{
			foreach(var instance in m_Instances) {
				if (instance == this)
					continue;

				instance.m_Pinned = new List<SceneEntry>(m_Pinned.Select(e => e.Clone()));
				instance.m_Scenes = new List<SceneEntry>(m_Scenes.Select(e => e.Clone()));
				instance.m_PersonalPrefs = m_PersonalPrefs.Clone();
				instance.m_ProjectPrefs = m_ProjectPrefs.Clone();
				instance.m_PinnedGroupsCount = m_PinnedGroupsCount;

				instance.RefreshColorizePatterns(instance.m_Pinned);
				instance.RefreshColorizePatterns(instance.m_Scenes);

				instance.Repaint();
			}
		}

		internal static void RepaintAllInstances()
		{
			foreach(var instance in m_Instances) {
				instance.Repaint();
			}
		}

		private void OnGUI()
		{
			m_SerializedObject.Update();

			// In case of upgrade while running.
			if (SceneOptionsPackButtonStyle == null && m_Initialized) {
				m_Initialized = false;
			}

			// Initialize on demand (not on OnEnable), to make sure everything is up and running.
			if (!m_Initialized || AssetsChanged) {
				if (!m_Initialized) {
					InitializeStyles();
				}

				InitializeData();

				// This instance will consume the AssetsChanged flag and synchronize the rest.
				if (AssetsChanged) {
					SynchronizeInstancesToMe();
				}

				m_Initialized = true;
				AssetsChanged = false;
			}

			if (m_ShowPreferences) {
				DrawPreferences();
				m_SerializedObject.ApplyModifiedProperties();
				return;
			}

			bool openFirstResult;
			string[] filterWords;
			DrawControls(out openFirstResult, out filterWords);

			DrawSceneLists(openFirstResult, filterWords);

			HandleScenesDrag();

			m_SerializedObject.ApplyModifiedProperties();
		}

		private void DrawControls(out bool openFirstResult, out string[] filterWords)
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

			//
			// Draw Filter
			//
			GUILayout.Label(SearchLabelContent, SearchLabelStyle, GUILayout.ExpandWidth(false));


			GUI.SetNextControlName("FilterControl");
			m_Filter = EditorGUILayout.TextField(m_Filter, SearchFieldStyle, GUILayout.Height(20));

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
			if (GUILayout.Button(" ", string.IsNullOrEmpty(m_Filter) ? SearchFieldCancelEmptyStyle : SearchFieldCancelStyle, GUILayout.Width(20.0f))) {
				m_Filter = "";
				GUI.FocusControl("");
				m_FocusFilterField = true;
				Repaint();
			}

			EditorGUI.BeginDisabledGroup(Application.isPlaying);

			if (GUILayout.Button(CreatePackageButtonContent, CreatePackageButtonStyle, GUILayout.Width(25.0f))) {
				if (SceneManager.sceneCount == 1) {
					EditorUtility.DisplayDialog("Create Pack of Scenes", "You need to have one or more scenes loaded additively.", "Ok");
				} else {
					CreateScenesPackage();
					GUIUtility.ExitGUI();
				}
			}

			if (GUILayout.Button(ReloadButtonContent, ToolbarButtonStyle, GUILayout.Width(25.0f))) {
				ReloadLoadedScenes();
				GUIUtility.ExitGUI();
			}

			EditorGUI.EndDisabledGroup();

			if (GUILayout.Button(QuickSortButtonContent, ToolbarButtonStyle, GUILayout.Width(25.0f))) {
				ShowQuickSortOptions();
				GUIUtility.ExitGUI();
			}

			if (GUILayout.Button(PreferencesButtonContent, ToolbarButtonStyle, GUILayout.Width(25.0f))) {
				m_ShowPreferences = true;
				Repaint();
				GUIUtility.ExitGUI();
			}

			// Unfocus on enter. Open first scene from results.
			openFirstResult = false;
			if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "FilterControl") {
				GUI.FocusControl("");
				Repaint();
				Event.current.Use();

				if (!string.IsNullOrEmpty(m_Filter)) {
					openFirstResult = true;
				}

			}

			EditorGUILayout.EndHorizontal();

			filterWords = string.IsNullOrEmpty(m_Filter) ? null : m_Filter.Split(FilterWordsSeparator, StringSplitOptions.RemoveEmptyEntries);
			if (openFirstResult) {
				m_Filter = "";
			}
		}

		private void DrawSceneLists(bool openFirstResult, string[] filterWords)
		{
			//
			// Show pinned scenes
			//
			if (m_Pinned.Count > 0) {
				GUILayout.Label("Pinned:", EditorStyles.boldLabel);

				// -3f is for clicks on the top edge of the splitter. It clicked on the button underneath.
				float scrollViewHeight = m_SplitterRect.y - CalcPinnedViewStartY() - 3f;

				//GUI.Box(new Rect(0, CalcPinnedViewStartY(), position.width, scrollViewHeight), "");

				EditorGUILayout.BeginVertical();
				m_ScrollPosPinned = EditorGUILayout.BeginScrollView(m_ScrollPosPinned, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Height(scrollViewHeight));

				for (int i = 0; i < m_Pinned.Count; ++i) {
					var sceneEntry = m_Pinned[i];
					var sceneName = Path.GetFileNameWithoutExtension(sceneEntry.Path);
					if (!IsFilteredOut(sceneName, filterWords)) {

						if (sceneEntry.FirstInGroup && filterWords == null) {
							if (m_PersonalPrefs.SpaceBetweenGroupsPinned > 0) {
								GUILayout.Space(m_PersonalPrefs.SpaceBetweenGroupsPinned);
							}
						}

						DrawSceneButtons(sceneEntry, true, filterWords == null, false);
					}
				}

				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();

				DrawSplitter();
			}

			//
			// Show all scenes
			//
			GUILayout.Label("Scenes:", EditorStyles.boldLabel);

			EditorGUILayout.BeginVertical();
			m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);

			var filteredCount = 0;
			for (var i = 0; i < m_Scenes.Count; i++) {
				var sceneEntry = m_Scenes[i];
				var sceneName = Path.GetFileNameWithoutExtension(sceneEntry.Path);

				// Do filtering
				if (IsFilteredOut(sceneName, filterWords))
					continue;

				filteredCount++;


				if (sceneEntry.FirstInGroup && filterWords == null) {
					if (m_PersonalPrefs.SpaceBetweenGroups > 0) {
						GUILayout.Space(m_PersonalPrefs.SpaceBetweenGroups);
					}
				}

				bool allowDrag = filterWords == null;
				allowDrag &= m_PersonalPrefs.SortType == SortType.MostRecent || m_PersonalPrefs.SortType == SortType.DontSort;

				DrawSceneButtons(sceneEntry, false, allowDrag, openFirstResult);
				openFirstResult = false;

				if (!m_ShowFullBigList && filteredCount >= m_PersonalPrefs.ListCountLimit)
					break;
			}


			// Big lists support
			if (!m_ShowFullBigList && filteredCount >= m_PersonalPrefs.ListCountLimit && GUILayout.Button("... Show All ...", GUILayout.ExpandWidth(true))) {
				m_ShowFullBigList = true;
				GUIUtility.ExitGUI();
			}

			if (m_ShowFullBigList && filteredCount >= m_PersonalPrefs.ListCountLimit && GUILayout.Button("... Hide Some ...", GUILayout.ExpandWidth(true))) {
				m_ShowFullBigList = false;
				GUIUtility.ExitGUI();
			}

			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void HandleScenesDrag()
		{
			if (m_DraggedEntity == null)
				return;

			if (Event.current.type == EventType.MouseUp) {
				m_DraggedEntity = null;

				StoreAllScenes();
				SynchronizeInstancesToMe();

				return;
			}

			Repaint();

			if (Event.current.type != EventType.Repaint)
				return;



			if (m_Pinned.Contains(m_DraggedEntity)) {

				var pinnedStartY = CalcPinnedViewStartY() + 3f - m_ScrollPosPinned.y;

				bool changed = HandleScenesListDrag(m_Pinned, pinnedStartY, m_PersonalPrefs.SpaceBetweenGroupsPinned);
				if (changed) {
					m_PinnedGroupsCount = RegroupScenes(m_Pinned);
				}

			} else {

				var scenesStartY = m_Pinned.Count == 0
					? CalcPinnedViewStartY() + 3f
					: m_SplitterRect.y + m_SplitterRect.height + EditorGUIUtility.singleLineHeight + 5f;
				scenesStartY -= m_ScrollPos.y;

				bool changed = HandleScenesListDrag(m_Scenes, scenesStartY, m_PersonalPrefs.SpaceBetweenGroups);
				if (changed) {
					SortScenes(m_Scenes, m_PersonalPrefs.SortType);
					RegroupScenes(m_Scenes);
				}
			}
		}

		private bool HandleScenesListDrag(List<SceneEntry> scenes, float startY, float groupSpace)
		{
			var entryRect = new Rect(0, startY, position.width, EditorGUIUtility.singleLineHeight + 4f);

			int groupsCount = 0;
			for (int i = 0; i < scenes.Count; ++i) {
				SceneEntry entry = scenes[i];

				if (entry.FirstInGroup) {
					groupsCount++;
				}
				entryRect.y = startY + entryRect.height * i + groupsCount * groupSpace;

				if (entryRect.Contains(Event.current.mousePosition)) {

					if (entry != m_DraggedEntity) {
						bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

						scenes.Remove(m_DraggedEntity);
						scenes.Insert(i, m_DraggedEntity);

						if (shouldAutoSnapSplitter) {
							AutoSnapSplitter();
						}

						return true;
					}

					return false;
				}
			}

			return false;
		}

		private bool InitializeSplitter(float startY)
		{
			bool shouldAutoSnapSplitter = false;

			if (startY < 0f) {
				startY = CalcPinnedViewStartY();
				shouldAutoSnapSplitter = true;
			}

			const float splitterHeight = 5f;
			m_SplitterRect = new Rect(0, startY, position.width, splitterHeight);

			return shouldAutoSnapSplitter;
		}

		private static float CalcPinnedViewStartY()
		{
			// Calculate pinned scroll view layout.
			const float LINE_PADDING = 4;
			float LINE_HEIGHT = EditorGUIUtility.singleLineHeight + LINE_PADDING;

			return LINE_HEIGHT * 2;
		}

		private float CalcSplitterMinY()
		{
			return CalcPinnedViewStartY() + EditorGUIUtility.singleLineHeight + 8f;
		}

		private float CalcSplitterMaxY()
		{
			float BOTTOM_PADDING = SceneButtonHeight * 3 + 8f;
			float minY = CalcSplitterMinY();
			var pinnedGroupsSpace = m_PinnedGroupsCount * m_PersonalPrefs.SpaceBetweenGroupsPinned;

			return Mathf.Min(minY + SceneButtonHeight * (m_Pinned.Count - 1) + pinnedGroupsSpace, position.height - BOTTOM_PADDING);
		}

		private void DrawSplitter()
		{
			m_SplitterRect.width = 150f;
			m_SplitterRect.x = (position.width - m_SplitterRect.width) / 2f;

			GUI.Box(m_SplitterRect, "- - - - - - -", SplitterStyle);
#if UNITY_2019_1_OR_NEWER
			EditorGUILayout.Space();	// Looks better for newer Unity version.
#endif
			//GUI.DrawTexture(m_SplitterRect, EditorGUIUtility.whiteTexture);
			EditorGUIUtility.AddCursorRect(m_SplitterRect, MouseCursor.ResizeVertical);

			if (Event.current.type == EventType.MouseDown && m_SplitterRect.Contains(Event.current.mousePosition)) {
				m_IsSplitterDragged = true;
			}

			float minY = CalcSplitterMinY();
			float maxY = CalcSplitterMaxY();

			if (m_IsSplitterDragged) {
				float splitterY = Mathf.Clamp(Event.current.mousePosition.y - Mathf.Round(m_SplitterRect.height / 2f), minY, maxY);
				m_SplitterRect.Set(m_SplitterRect.x, splitterY, m_SplitterRect.width, m_SplitterRect.height);
				Repaint();
			} else {
				// Sync with window size every time.
				m_SplitterRect.y = Mathf.Max(minY, Mathf.Min(m_SplitterRect.y, maxY));
			}

			if (m_IsSplitterDragged && Event.current.type == EventType.MouseUp) {
				m_IsSplitterDragged = false;
				m_PersonalPrefs.SplitterY = m_SplitterRect.y;
				StorePersonalPrefs();
			}
		}

		private bool ShouldAutoSnapSplitter()
		{
			float maxY = CalcSplitterMaxY();

			return maxY - m_SplitterRect.y < 6f;
		}

		private void AutoSnapSplitter()
		{
			m_SplitterRect.y = CalcSplitterMaxY();

			m_PersonalPrefs.SplitterY = m_SplitterRect.y;
			StorePersonalPrefs();
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

		private void MoveSceneAtTopOfList(SceneEntry sceneEntry)
		{
			int idx = m_Scenes.IndexOf(sceneEntry);
			if (idx >= 0) {
				m_Scenes.RemoveAt(idx);
				m_Scenes.Insert(0, sceneEntry);
			}

			RegroupScenes(m_Scenes);
		}

		private void DrawSceneButtons(SceneEntry sceneEntry, bool isPinned, bool allowDrag, bool forceOpen)
		{
			EditorGUILayout.BeginHorizontal();

			SceneButtonContentCache.text = sceneEntry.DisplayName;
			SceneButtonContentCache.tooltip = sceneEntry.Path;
			if (sceneEntry.IsPack) {
				var tooltipBuilder = new StringBuilder();
				tooltipBuilder.AppendLine("Pack of scenes:");
				foreach(var packedScene in sceneEntry.PackedScenes) {
					tooltipBuilder.AppendLine(packedScene.Path);
				}

				SceneButtonContentCache.tooltip = tooltipBuilder.ToString();
			}

			var prevBackgroundColor = GUI.backgroundColor;
			var prevColor = SceneButtonStyle.normal.textColor;
			if (sceneEntry.ColorizePattern != null && !string.IsNullOrEmpty(sceneEntry.ColorizePattern.Patterns)) {
				GUI.backgroundColor = sceneEntry.ColorizePattern.BackgroundColor;
				Color textColor = sceneEntry.ColorizePattern.TextColor;

				if (EditorGUIUtility.isProSkin) {
					GUI.backgroundColor += new Color(0.4f, 0.4f, 0.4f, 0f);
				}

				GUI.contentColor = textColor;

				foreach(GUIStyle style in m_SceneButtonStyles) {
					style.normal.textColor
					= style.hover.textColor
					= style.active.textColor
					= style.focused.textColor
					= textColor;
				}
			}

			if (sceneEntry == m_DraggedEntity) {
				GUI.backgroundColor *= new Color(0.9f, 0.9f, 0.9f, 0.6f);
			}

			Scene scene = sceneEntry.IsPack ? default : SceneManager.GetSceneByPath(sceneEntry.Path);
			bool isSceneLoaded = scene.IsValid();
			bool isActiveScene = isSceneLoaded && scene == SceneManager.GetActiveScene();
			var loadedButton = isSceneLoaded ? (isActiveScene ? SceneLoadedButtonActiveContent : SceneLoadedButtonRemoveContent) : SceneLoadedButtonAddContent;

			bool optionsPressed = GUILayout.Button(sceneEntry.IsPack ? SceneOptionsPackButtonContent : SceneOptionsButtonContent, sceneEntry.IsPack ? SceneOptionsPackButtonStyle : SceneOptionsButtonStyle, GUILayout.Width(22));
			bool scenePressed = GUILayout.Button(SceneButtonContentCache, SceneButtonStyle) || forceOpen;
			var dragRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(5f), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2f));
			EditorGUI.BeginDisabledGroup(sceneEntry.IsPack);
			bool playPressed = GUILayout.Button(ScenePlayButtonContent, ScenePlayButtonStyle, GUILayout.Width(20));
			EditorGUI.EndDisabledGroup();
			bool loadPressed = GUILayout.Button(loadedButton, SceneLoadedButtonStyle, GUILayout.Width(21));

			if (allowDrag) {
				float paddingTop = Mathf.Floor(EditorGUIUtility.singleLineHeight / 2);
				dragRect.y += paddingTop;
				GUI.Box(dragRect, string.Empty, DragHandlerStyle);
				dragRect.y -= paddingTop;
				EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeVertical);

				if (Event.current.type == EventType.MouseDown && dragRect.Contains(Event.current.mousePosition)) {
					m_DraggedEntity = sceneEntry;
				}
			}

			GUI.backgroundColor = prevBackgroundColor;

			GUI.contentColor = prevColor;

			foreach (GUIStyle style in m_SceneButtonStyles) {
				style.normal.textColor
				= style.hover.textColor
				= style.active.textColor
				= style.focused.textColor
				= prevColor;
			}

			if (scenePressed || optionsPressed || playPressed || loadPressed) {
				// If scene was removed outside of Unity, the AssetModificationProcessor would not get notified.
				if (!File.Exists(sceneEntry.Path)) {
					AssetsChanged = true;
					return;
				}
			}

			if (scenePressed) {


				if (Event.current.shift) {
					EditorUtility.RevealInFinder(sceneEntry.Path);

				} else if (Application.isPlaying || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {

					if (Application.isPlaying) {
						if (sceneEntry.IsPack) {
							EditorUtility.DisplayDialog("Load Pack of Scenes", "Cannot load pack of scenes while in Play mode.", "Ok");
						} else {
							// Note: to do this, the scene must me added to the build settings list.
							// Note2: Sometimes there are side effects with the lighting.
							SceneManager.LoadSceneAsync(sceneEntry.Path);
						}
					} else {
						if (sceneEntry.IsPack) {
							LoadPackedScenes(sceneEntry.PackedScenes, false);
						} else {
							EditorSceneManager.OpenScene(sceneEntry.Path);
						}
					}

					if (m_PersonalPrefs.SortType == SortType.MostRecent) {
						MoveSceneAtTopOfList(sceneEntry);
					}
					//m_Filter = "";	// It's a feature. Sometimes you need to press on multiple scenes in a row.
					GUI.FocusControl("");
				}
			}


			if (optionsPressed) {
				ShowSceneOptions(isPinned, sceneEntry);
			}

			if (playPressed) {
				PlaySceneDirectly(sceneEntry);
			}

			if (loadPressed) {
				if (Application.isPlaying) {

					if (sceneEntry.IsPack) {
						EditorUtility.DisplayDialog("Load Pack of Scenes", "Cannot load pack of scenes while in Play mode.", "Ok");
						GUIUtility.ExitGUI();
					}

					if (!isSceneLoaded) {
						// Note: to do this, the scene must me added to the build settings list.
						SceneManager.LoadScene(sceneEntry.Path, LoadSceneMode.Additive);
					} else if (!isActiveScene) {
						SceneManager.UnloadSceneAsync(scene);
					}
				} else {
					if (sceneEntry.IsPack) {
						LoadPackedScenes(sceneEntry.PackedScenes, true);
						GUIUtility.ExitGUI();
					}

					if (!isSceneLoaded) {
						EditorSceneManager.OpenScene(sceneEntry.Path, OpenSceneMode.Additive);

						//if (m_PersonalPrefs.SortType == SortType.MostRecent) {
						//	MoveSceneAtTopOfList(sceneEntry);
						//}
					} else if (!isActiveScene) {
						EditorSceneManager.CloseScene(scene, true);
					}
				}
			}

			EditorGUILayout.EndHorizontal();
		}

		private void ShowSceneOptions(bool isPinned, SceneEntry sceneEntry)
		{
			var menu = new GenericMenu();
			int index = isPinned ? m_Pinned.IndexOf(sceneEntry) : m_Scenes.IndexOf(sceneEntry);

			foreach (SceneOptions value in Enum.GetValues(typeof(SceneOptions))) {

				if (sceneEntry.IsPack && value == SceneOptions.Exclude)
					continue;

				if (value == SceneOptions.Exclude) {
					menu.AddItem(new GUIContent($"Exclude/Exclude Scene"), false, OnExcludeOption, sceneEntry.Path);
					menu.AddItem(new GUIContent($"Exclude/Exclude Folder"), false, OnExcludeOption, Path.GetDirectoryName(sceneEntry.Path).Replace("\\", "/"));
					continue;
				}

				if (value == SceneOptions.Colorize) {
					foreach (ColorizeOptions colorValue in Enum.GetValues(typeof(ColorizeOptions))) {
						if (colorValue == ColorizeOptions.Clear) {
							menu.AddSeparator("Colorize/");
						}

						menu.AddItem(new GUIContent($"Colorize/{ObjectNames.NicifyVariableName(colorValue.ToString())}"), false, OnColorizeOption, MakeKVP(colorValue, sceneEntry));
					}
					continue;
				}

				string menuName = ObjectNames.NicifyVariableName(value.ToString());
				if (value == SceneOptions.Pin && isPinned) {
					menuName = sceneEntry.IsPack ? "Remove Pack" : "Unpin";
				}

				var handler = isPinned ? (GenericMenu.MenuFunction2) OnSelectPinnedOption : OnSelectUnpinnedOption;
				menu.AddItem(new GUIContent(menuName), false, handler, MakeKVP(value, index));
			}

			menu.ShowAsContext();
		}

		private void OnSelectPinnedOption(object data)
		{
			var pair = (KeyValuePair<SceneOptions, int>)data;
			int index = pair.Value;

			bool shouldAutoSnapSplitter = false;
			var sceneEntry = m_Pinned[index];

			switch (pair.Key) {

				case SceneOptions.Pin:
					if (m_ProjectPrefs.ScenePacks.Any(sp => SceneEntry.PackedSceneGuidPrefix + sp.Identifier == sceneEntry.Guid)) {
						EditorUtility.DisplayDialog("Pack of Scenes", "Can't remove this pack as it comes from the project preferences. Discuss this with your team and remove it from there.", "Ok");
						return;
					}

					shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

					m_Pinned.RemoveAt(index);

					if (!sceneEntry.IsPack) {
						int unpinIndex = m_Scenes.FindIndex(s => s.Folder == sceneEntry.Folder);
						if (unpinIndex == -1) {
							m_Scenes.Insert(0, sceneEntry);
						} else {
							m_Scenes.Insert(unpinIndex, sceneEntry);
						}
					} else {
						// Remove Colorize associated with this pack.
						if (UpdatePreferenceLists()) {
							StorePersonalPrefs();
						}
					}

					break;

				case SceneOptions.MoveFirst:
					m_Pinned.Insert(0, m_Pinned[index]);
					m_Pinned.RemoveAt(index + 1);
					break;

				case SceneOptions.MoveLast:
					m_Pinned.Add(m_Pinned[index]);
					m_Pinned.RemoveAt(index);
					break;

				case SceneOptions.CopyAssetPath:
					if (sceneEntry.IsPack) {
						EditorGUIUtility.systemCopyBuffer = string.Join("\n", sceneEntry.PackedScenes.Select(s => s.Path));
					} else {
						EditorGUIUtility.systemCopyBuffer = sceneEntry.Path;
					}
					return;

				case SceneOptions.ShowInExplorer:
					EditorUtility.RevealInFinder(sceneEntry.Path);
					return;

				case SceneOptions.ShowInProject:
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sceneEntry.Path));
					return;
			}

			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private void OnSelectUnpinnedOption(object data)
		{
			var pair = (KeyValuePair<SceneOptions, int>)data;
			int index = pair.Value;

			bool shouldAutoSnapSplitter = false;
			var sceneEntry = m_Scenes[index];

			switch (pair.Key) {

				case SceneOptions.Pin:
					shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

					m_Scenes.RemoveAt(index);

					int pinIndex = m_Pinned.FindLastIndex(s => s.Folder == sceneEntry.Folder);
					if (pinIndex == -1) {
						m_Pinned.Add(sceneEntry);
					} else {
						m_Pinned.Insert(pinIndex + 1, sceneEntry);
					}

					break;

				case SceneOptions.MoveFirst:
					m_Scenes.Insert(0, m_Scenes[index]);
					m_Scenes.RemoveAt(index + 1);
					break;

				case SceneOptions.MoveLast:
					m_Scenes.Add(m_Scenes[index]);
					m_Scenes.RemoveAt(index);
					break;

				case SceneOptions.CopyAssetPath:
					if (sceneEntry.IsPack) {
						EditorGUIUtility.systemCopyBuffer = string.Join("\n", sceneEntry.PackedScenes.Select(s => s.Path));
					} else {
						EditorGUIUtility.systemCopyBuffer = sceneEntry.Path;
					}
					return;

				case SceneOptions.ShowInExplorer:
					EditorUtility.RevealInFinder(sceneEntry.Path);
					return;

				case SceneOptions.ShowInProject:
					EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sceneEntry.Path));
					return;
			}

			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private void OnExcludeOption(object data)
		{
			string scenePath = (string)data;
			string guid = AssetDatabase.AssetPathToGUID(scenePath);
			m_PersonalPrefs.Exclude.Add(scenePath + "|" + guid);
			StorePersonalPrefs();
			AssetsChanged = true;
		}

		private void OnColorizeOption(object data)
		{
			var pair = (KeyValuePair<ColorizeOptions, SceneEntry>)data;
			SceneEntry sceneEntry = pair.Value;

			ColorizePattern colorPattern;

			Color backgroundColor = Color.white;
			Color textColor = Color.black;

			switch(pair.Key) {
				case ColorizeOptions.Red:
					backgroundColor = new Color(0.7f, 0.3745f, 0.3745f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Green:
					backgroundColor = new Color(0.2817f, 0.549f, 0.2945f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Blue:
					backgroundColor = new Color(0.2875f, 0.5648f, 0.611f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Orange:
					backgroundColor = new Color(0.775f, 0.556f, 0.309f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Yellow:
					backgroundColor = new Color(0.634f, 0.6267f, 0.1725f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Brown:
					backgroundColor = new Color(0.6529f, 0.5431f, 0.3392f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Purple:
					backgroundColor = new Color(0.547f, 0.28f, 0.4888f);
					textColor = Color.white;
					break;

				case ColorizeOptions.Clear:

					colorPattern = sceneEntry.IsPack
						? m_PersonalPrefs.ColorizePatterns.FirstOrDefault(cp => cp.Patterns.Contains(sceneEntry.Guid))
						: m_PersonalPrefs.ColorizePatterns.FirstOrDefault(cp => cp.Patterns.Contains(sceneEntry.Path) && !cp.Patterns.Contains($"|{SceneEntry.PackedSceneGuidPrefix}"))
						;

					if (colorPattern == null) {
						colorPattern = GetMatchedColorPattern(sceneEntry, m_PersonalPrefs.ColorizePatterns);

						if (colorPattern != null) {
							bool choice = EditorUtility.DisplayDialog(
								"Clear Scene Colorization",
								$"Selected scene is included in a bigger colorize pattern:\n\"{colorPattern.Patterns}\"\nDo you want to clear the original pattern?",
								"Clear the original pattern", "Cancel"
								);

							if (!choice)
								return;
						} else {
							colorPattern = GetMatchedColorPattern(sceneEntry, m_ProjectPrefs.ColorizePatterns);
							EditorUtility.DisplayDialog(
								"Clear Scene Colorization",
								$"Selected scene is included in a project-wide colorize pattern:\n\"{colorPattern.Patterns}\"\nDiscuss this with your team, then change the pattern in the project preferences.",
								"Ok"
								);
						}
					}

					if (colorPattern != null) {
						m_PersonalPrefs.ColorizePatterns.Remove(colorPattern);

						RefreshColorizePatterns(m_Scenes);
						RefreshColorizePatterns(m_Pinned);

						SynchronizeInstancesToMe();

						StorePersonalPrefs();
					}
					return;

				case ColorizeOptions.Custom:
					m_ShowPreferences = true;
					Repaint();
					return;

				default:
					throw new ArgumentException(pair.Key.ToString());

			}

			colorPattern = sceneEntry.IsPack
				? m_PersonalPrefs.ColorizePatterns.FirstOrDefault(cp => cp.Patterns.Contains(sceneEntry.Guid))
				: m_PersonalPrefs.ColorizePatterns.FirstOrDefault(cp => cp.Patterns.Contains(sceneEntry.Path) && !cp.Patterns.Contains($"|{SceneEntry.PackedSceneGuidPrefix}"))
				;

			if (colorPattern == null) {
				colorPattern = GetMatchedColorPattern(sceneEntry, m_PersonalPrefs.ColorizePatterns);

				if (colorPattern != null) {
					int choice = EditorUtility.DisplayDialogComplex(
						"Colorize scene",
						$"Selected scene is included in another colorize pattern:\n\"{colorPattern.Patterns}\"\nDo you want to colorize ONLY the selected scene or change the original pattern colors?",
						"Scene only", "Cancel", "Original folder pattern"
						);
					if (choice == 1)
						return;

					if (choice == 0) {
						colorPattern = null;
					}
				}

				if (colorPattern == null) {
					colorPattern = new ColorizePattern() {
						Patterns = sceneEntry.Path + "|" + sceneEntry.Guid,	// NOTE: use the original path if newly created.
						BackgroundColor = Color.white,
						TextColor = Color.black,
					};
					m_PersonalPrefs.ColorizePatterns.Add(colorPattern);
				}
			}

			colorPattern.BackgroundColor = backgroundColor;
			colorPattern.TextColor = textColor;

			RefreshColorizePatterns(m_Scenes);
			RefreshColorizePatterns(m_Pinned);

			SynchronizeInstancesToMe();

			StorePersonalPrefs();
		}

		private void PlaySceneDirectly(SceneEntry sceneEntry)
		{
			if (EditorApplication.isPlaying) {
				SceneManager.LoadSceneAsync(sceneEntry.Path);
				return;
			}

			m_LaunchedSceneDirectly = true;
			m_PreviousStartScene = EditorSceneManager.playModeStartScene;

			// YES! This exists from Unity 2017!!!! Bless you!!!
			EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(sceneEntry.Path);
			EditorApplication.isPlaying = true;

		}

		private bool ClearDirectPlayScene()
		{
			if (m_LaunchedSceneDirectly) {
				m_LaunchedSceneDirectly = false;

				// Because this property survives the assembly reload, leaving normal play button stuck on our scene.
				EditorSceneManager.playModeStartScene = m_PreviousStartScene;
				m_PreviousStartScene = null;
				return true;
			}

			return false;
		}

		private void CreateScenesPackage()
		{
			List<PackedSceneState> packedScenes = CreatePackedScenesFromCurrent();

			if (packedScenes.Count <= 1) {
				GUIUtility.ExitGUI();
			}

			var guid = SceneEntry.PackedSceneGuidPrefix + GUID.Generate().ToString();
			var entry = new SceneEntry(guid, packedScenes);

			bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();
			m_Pinned.Insert(0, entry);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			RefreshDisplayNames(m_Pinned);

			RefreshColorizePatterns(m_Pinned);

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private static List<PackedSceneState> CreatePackedScenesFromCurrent()
		{
			Scene activeScene = SceneManager.GetActiveScene();

			var packedScenes = new List<PackedSceneState>();

			for (int i = 0; i < SceneManager.sceneCount; ++i) {
				Scene scene = SceneManager.GetSceneAt(i);

				PackedSceneLoadedState loadedState = scene.isLoaded
					? PackedSceneLoadedState.Loaded
					: PackedSceneLoadedState.Unloaded
					;

				if (scene == activeScene) {
					loadedState = PackedSceneLoadedState.MainScene;
				}

				var packedScene = new PackedSceneState() {
					Guid = AssetDatabase.AssetPathToGUID(scene.path),
					Path = scene.path,
					LoadedState = loadedState,
				};

				// "Unknown" unsaved scenes.
				if (string.IsNullOrEmpty(packedScene.Guid))
					continue;

				packedScenes.Add(packedScene);
			}

			// This can happen if the "Unknown" unsaved scene is the main one.
			if (!packedScenes.Any(ps => ps.IsMainScene) && packedScenes.Count > 0) {
				PackedSceneState packedScene = packedScenes[0];
				packedScene.LoadedState = PackedSceneLoadedState.MainScene;
				packedScenes[0] = packedScene;
			}

			return packedScenes;
		}

		private static void LoadPackedScenes(IList<PackedSceneState> packedScenes, bool additive)
		{
			string scenePath = AssetDatabase.GUIDToAssetPath(packedScenes[0].Guid);
			if (string.IsNullOrEmpty(scenePath))
				return;

			EditorSceneManager.OpenScene(scenePath, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
			int activeSceneIndex = 0;

			for (int i = 1 /* 0 is loaded */; i < packedScenes.Count; ++i) {
				var packedScene = packedScenes[i];
				scenePath = AssetDatabase.GUIDToAssetPath(packedScene.Guid);
				if (string.IsNullOrEmpty(scenePath))
					continue;

				EditorSceneManager.OpenScene(scenePath, packedScene.IsLoaded ? OpenSceneMode.Additive : OpenSceneMode.AdditiveWithoutLoading);
				if (packedScene.IsMainScene) {
					activeSceneIndex = i;
				}
			}

			if (!additive) {
				// In case the active scene was not the first one.
				SceneManager.SetActiveScene(SceneManager.GetSceneAt(activeSceneIndex));
			}

			// First scene could have been unloaded, but we left it loaded.
			if (!packedScenes[0].IsLoaded) {
				EditorSceneManager.CloseScene(SceneManager.GetSceneAt(0), false);
			}
		}

		private static void ReloadLoadedScenes()
		{
			if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
				List<PackedSceneState> packedScenes = CreatePackedScenesFromCurrent();
				LoadPackedScenes(packedScenes, false);
			}
		}

		private void ShowQuickSortOptions()
		{
			var menu = new GenericMenu();

			Action<string, bool, List<SceneEntry>, QuickSortType> addItem = (menuPath, enabled, scenes, sortType) => {
				if (enabled) {
					menu.AddItem(new GUIContent(menuPath), false, () => OnSelectQuickSortOption(scenes, sortType));
				} else {
#if UNITY_2017
					menu.AddItem(new GUIContent(menuPath), false, null);
#else
					menu.AddDisabledItem(new GUIContent(menuPath), false);
#endif
				}
			};

			addItem("Sort Pinned/By Path", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByPath);
			addItem("Sort Pinned/By Filename", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByFileName);
			addItem("Sort Pinned/By File Size", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByFileSize);
			addItem("Sort Pinned/By Last Modified", m_Pinned.Count > 0, m_Pinned, QuickSortType.ByDateModified);

			var canSort = m_PersonalPrefs.SortType == SortType.MostRecent || m_PersonalPrefs.SortType == SortType.DontSort;
			addItem("Sort Unpinned/By Path", canSort, m_Scenes, QuickSortType.ByPath);
			addItem("Sort Unpinned/By Filename", canSort, m_Scenes, QuickSortType.ByFileName);
			addItem("Sort Unpinned/By File Size", canSort, m_Scenes, QuickSortType.ByFileSize);
			addItem("Sort Unpinned/By Last Modified", canSort, m_Scenes, QuickSortType.ByDateModified);

			if (m_Pinned.Count > 0) {
				menu.AddItem(new GUIContent("Clear All Pinned Scenes"), false, () => {
					if (EditorUtility.DisplayDialog("Clear Pinned Scenes", "Are you sure you want to clear all pinned scenes?", "Yes!", "No")) {
						ClearPinned();
					}
				});
			} else {
#if UNITY_2017
				menu.AddItem(new GUIContent("Clear All Pinned Scenes"), false, null);
#else
				menu.AddDisabledItem(new GUIContent("Clear All Pinned Scenes"), false);
#endif
			}

			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Cancel"), false, () => { });

			menu.ShowAsContext();
		}

		private void OnSelectQuickSortOption(List<SceneEntry> scenes, QuickSortType sortType)
		{
			bool shouldAutoSnapSplitter = ShouldAutoSnapSplitter();

			switch(sortType) {
				case QuickSortType.ByPath:
					SortScenes(scenes, SortType.ByPath);
					break;

				case QuickSortType.ByFileName:
					SortScenes(scenes, SortType.ByFileName);
					break;

				case QuickSortType.ByFileSize:
					scenes.Sort((a, b) => new FileInfo(b.Path).Length.CompareTo(new FileInfo(a.Path).Length));
					break;

				case QuickSortType.ByDateModified:
					scenes.Sort((a, b) => File.GetLastWriteTime(b.Path).CompareTo(File.GetLastWriteTime(a.Path)));
					break;
			}

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			if (shouldAutoSnapSplitter) {
				AutoSnapSplitter();
			}

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private void ClearPinned()
		{
			while (m_Pinned.Count > 0) {
				var sceneEntry = m_Pinned[0];
				m_Pinned.RemoveAt(0);

				int unpinIndex = m_Scenes.FindIndex(s => s.Folder == sceneEntry.Folder);
				if (unpinIndex == -1) {
					m_Scenes.Insert(0, sceneEntry);
				} else {
					m_Scenes.Insert(unpinIndex, sceneEntry);
				}
			}

			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RegroupScenes(m_Scenes);
			m_PinnedGroupsCount = RegroupScenes(m_Pinned);

			AutoSnapSplitter();

			StoreAllScenes();
			SynchronizeInstancesToMe();

			Repaint();
		}

		private readonly GUIContent PreferencesColorizePatternsLabelContent = new GUIContent("Colorize Entries", "Set colors of scenes based on a folder or name patterns.");
		private readonly GUIContent PreferencesScenePacksLabelContent = new GUIContent("Scene Packs", "Set scene packs that will always be available for your teammates to use.");
		private readonly GUIContent PreferencesExcludePatternsLabelContent = new GUIContent("Exclude Scenes", "Relative path (contains '/') or asset name to be ignored.\nAppend '|' then guid to track the assets when renamed.");
		private Vector2 m_PreferencesScroll;

		private PreferencesTab m_SelectedTab = PreferencesTab.Personal;
		private static readonly string[] m_PreferencesTabsNames = Enum.GetNames(typeof(PreferencesTab));

		private void DrawPreferences()
		{
			EditorGUILayout.Space();

			//
			// Save / Close Buttons
			//
			EditorGUILayout.BeginHorizontal();
			{
				EditorGUILayout.LabelField("Save changes:", EditorStyles.boldLabel);

				GUILayout.FlexibleSpace();

				var prevColor = GUI.backgroundColor;
				GUI.backgroundColor = Color.green / 1.2f;
				if (GUILayout.Button("Save And Close", GUILayout.MaxWidth(150f))) {
					SaveAndClosePreferences();

					GUI.FocusControl("");
					EditorGUIUtility.ExitGUI();
				}
				GUI.backgroundColor = prevColor;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();

			m_SelectedTab = (PreferencesTab) GUILayout.Toolbar((int) m_SelectedTab, m_PreferencesTabsNames);

			m_PreferencesScroll = EditorGUILayout.BeginScrollView(m_PreferencesScroll);

			switch (m_SelectedTab) {
				case PreferencesTab.Personal:
					DrawPersonalPreferences();
					break;
				case PreferencesTab.Project:
					DrawProjectPreferences();
					break;
				case PreferencesTab.About:
					DrawAboutPreferences();
					break;
			}

			GUILayout.Space(12f);

			EditorGUILayout.EndScrollView();
		}

		private void SaveAndClosePreferences()
		{
			var splitterChars = new char[] { ';' };

			m_PersonalPrefs.DisplayRemoveFolders.RemoveAll(string.IsNullOrWhiteSpace);
			m_PersonalPrefs.Exclude = m_PersonalPrefs.Exclude
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.Select(e => e.Replace('\\', '/').Trim())
				.ToList();
			m_PersonalPrefs.ColorizePatterns = m_PersonalPrefs.ColorizePatterns
				.Where(c => !string.IsNullOrWhiteSpace(c.Patterns))
				.Select(c => { c.Patterns = c.Patterns.Replace('\\', '/').Trim(); return c; })
				.ToList();


			m_ProjectPrefs.Exclude = m_ProjectPrefs.Exclude
				.Where(e => !string.IsNullOrWhiteSpace(e))
				.Select(e => e.Replace('\\', '/').Trim())
				.ToList();
			m_ProjectPrefs.ColorizePatterns = m_ProjectPrefs.ColorizePatterns
				.Where(c => !string.IsNullOrWhiteSpace(c.Patterns))
				.Select(c => { c.Patterns = c.Patterns.Replace('\\', '/').Trim(); return c; })
				.ToList();

			// Sort explicitly, so assets will change on reload.
			SortScenes(m_Scenes, m_PersonalPrefs.SortType);

			RefreshDisplayNames(m_Scenes);
			RefreshDisplayNames(m_Pinned);

			RefreshColorizePatterns(m_Scenes);
			RefreshColorizePatterns(m_Pinned);

			StorePersonalPrefs();
			StoreProjectPrefs();

			m_ShowPreferences = false;
			m_SelectedTab = PreferencesTab.Personal;
			AssetsChanged = true;
		}

		private void DrawPersonalPreferences()
		{
			EditorGUILayout.HelpBox("These are personal preferences, stored in the UserSettings folder.\n" +
				"Hint: you can type in folders instead of specific scene assets.\n" +
				"For more info check the tooltips.",
				MessageType.Info);

			m_PersonalPrefs.SortType = (SortType)EditorGUILayout.EnumPopup(new GUIContent("Sort by", "How to automatically sort the list of scenes (not the pinned ones).\nNOTE: Changing this will loose the \"Most Recent\" sort done by now."), m_PersonalPrefs.SortType);
			m_PersonalPrefs.SceneDisplay = (SceneDisplay)EditorGUILayout.EnumPopup(new GUIContent("Display entries", "How scenes should be displayed."), m_PersonalPrefs.SceneDisplay);

			if (m_PersonalPrefs.SceneDisplay == SceneDisplay.SceneNamesWithParents) {
				EditorGUI.indentLevel++;
				m_PersonalPrefs.DisplayParentsCount = EditorGUILayout.IntField(new GUIContent("Parents depth", "How many parent folders to show.\nExample: Assets/Scenes/GUI/Foo.unity\n> 1 displays \"GUI/Foo\"\n> 2 displays \"Scenes/GUI/Foo\"\netc..."), m_PersonalPrefs.DisplayParentsCount);
				m_PersonalPrefs.DisplayParentsCount = Mathf.Clamp(m_PersonalPrefs.DisplayParentsCount, 1, 1024); // More round.
				EditorGUI.indentLevel--;
			}

			if (m_PersonalPrefs.SceneDisplay == SceneDisplay.SceneFullPathsOmitFolders) {
				EditorGUI.indentLevel++;

				var sp = m_SerializedObject.FindProperty("m_PersonalPrefs").FindPropertyRelative("DisplayRemoveFolders");

				EditorGUILayout.PropertyField(sp, new GUIContent("Omit folders", "List of folders that will be removed from the displayed path. Example: Remove \"Assets\" folder from the path."), true);

				EditorGUI.indentLevel--;
			}

			m_PersonalPrefs.ListCountLimit = EditorGUILayout.IntField(new GUIContent("Shown scenes limit", "If the scenes in the list are more than this value, they will be truncated (button \"Show All\" is shown).\nTruncated scenes still participate in the search.\n\nThis is very useful in a project with lots of scenes, where drawing large scrollable lists is expensive."), m_PersonalPrefs.ListCountLimit);
			m_PersonalPrefs.ListCountLimit = Mathf.Clamp(m_PersonalPrefs.ListCountLimit, 0, 1024); // More round.

			const string spaceBetweenGroupsHint = "Space in pixels added before every group of scenes.\nScenes in the same folder are considered as a group.";
			m_PersonalPrefs.SpaceBetweenGroups = EditorGUILayout.IntField(new GUIContent("Padding for groups", spaceBetweenGroupsHint), m_PersonalPrefs.SpaceBetweenGroups);
			m_PersonalPrefs.SpaceBetweenGroups = Mathf.Clamp(m_PersonalPrefs.SpaceBetweenGroups, 0, (int)EditorGUIUtility.singleLineHeight);

			m_PersonalPrefs.SpaceBetweenGroupsPinned = EditorGUILayout.IntField(new GUIContent("Padding for pinned groups", spaceBetweenGroupsHint), m_PersonalPrefs.SpaceBetweenGroupsPinned);
			m_PersonalPrefs.SpaceBetweenGroupsPinned = Mathf.Clamp(m_PersonalPrefs.SpaceBetweenGroupsPinned, 0, 16); // More round.

			// Colorize Patterns
			{
				var sp = m_SerializedObject.FindProperty("m_PersonalPrefs");

				EditorGUILayout.PropertyField(sp.FindPropertyRelative("ColorizePatterns"), PreferencesColorizePatternsLabelContent, true);
				EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), PreferencesExcludePatternsLabelContent, true);


				foreach (var cf in m_PersonalPrefs.ColorizePatterns) {
					if (string.IsNullOrEmpty(cf.Patterns)) {
						cf.BackgroundColor = Color.white;
						cf.TextColor = Color.black;
					}
				}
			}
		}

		private void DrawProjectPreferences()
		{
			EditorGUILayout.HelpBox("These settings will be saved in the ProjectSettings folder.\nFeel free to add them to your version control system.\nCoordinate any changes here with your team.", MessageType.Warning);

			var sp = m_SerializedObject.FindProperty("m_ProjectPrefs");

			EditorGUILayout.PropertyField(sp.FindPropertyRelative("ColorizePatterns"), PreferencesColorizePatternsLabelContent, true);
			EditorGUILayout.PropertyField(sp.FindPropertyRelative("Exclude"), PreferencesExcludePatternsLabelContent, true);
			EditorGUILayout.PropertyField(sp.FindPropertyRelative("ScenePacks"), PreferencesScenePacksLabelContent, true);
			if (GUILayout.Button("Add pack from currently loaded scenes")) {
				if (SceneManager.sceneCount == 1) {
					EditorUtility.DisplayDialog("Create Pack of Scenes", "You need to have one or more scenes loaded additively.", "Ok");
					GUIUtility.ExitGUI();
				}

				var packedPreference = new PackedScenesPreference() {
					Identifier = GUID.Generate().ToString(),
					PackedStates = CreatePackedScenesFromCurrent(),
				};

				if (packedPreference.PackedStates.Count <= 1) {
					GUIUtility.ExitGUI();
				}

				var window = (ScenesInProject)m_SerializedObject.targetObject;
				window.m_ProjectPrefs.ScenePacks.Add(packedPreference);
				EditorUtility.SetDirty(window);
				GUIUtility.ExitGUI();
			}

			foreach (var cf in m_ProjectPrefs.ColorizePatterns) {
				if (string.IsNullOrEmpty(cf.Patterns)) {
					cf.BackgroundColor = Color.white;
					cf.TextColor = Color.black;
				}
			}
		}

		private void DrawAboutPreferences()
		{
			EditorGUILayout.LabelField("About:", EditorStyles.boldLabel);

			var urlStyle = new GUIStyle(EditorStyles.label);
			urlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
			urlStyle.active.textColor = Color.red;

			const string mail = "NibbleByte3@gmail.com";

			GUILayout.Label("Created by Filip Slavov", GUILayout.ExpandWidth(false));
			if (GUILayout.Button(mail, urlStyle, GUILayout.ExpandWidth(false))) {
				Application.OpenURL("mailto:" + mail);
			}

			if (GUILayout.Button("Help / Documentation", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var assets = AssetDatabase.FindAssets("ScenesInProject-Documentation");
				if (assets.Length == 0) {
					EditorUtility.DisplayDialog("Documentation missing!", "The documentation you requested is missing.", "Ok");
				} else {
					Application.OpenURL(System.Environment.CurrentDirectory + "/"+ AssetDatabase.GUIDToAssetPath(assets[0]));
				}
			}

			if (GUILayout.Button("Plugin at Asset Store", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var githubURL = "https://assetstore.unity.com/packages/tools/utilities/scenes-in-project-169933";
				Application.OpenURL(githubURL);
			}

			if (GUILayout.Button("Source at GitHub", GUILayout.MaxWidth(EditorGUIUtility.labelWidth))) {
				var githubURL = "https://github.com/NibbleByte/UnityAssetManagementTools";
				Application.OpenURL(githubURL);
			}
		}

		// NOTE: Copy pasted from SearchAssetsFilter.
		private static bool ShouldExclude(string guid, string path, IEnumerable<string> excludes)
		{
			foreach(var exclude in excludes) {

				bool isExcludePath = exclude.Contains('/');    // Check if this is a path or just a filename

				if (isExcludePath) {
					// NOTE: excluded dirs can also have guids and will be tracked.

					int guidSeparatorIndex = exclude.LastIndexOf('|');
					if (guidSeparatorIndex != -1) {
						string patternGuid = exclude.Substring(guidSeparatorIndex + 1);
						if (guid == patternGuid) {
							return true;
						}
					} else {
						guidSeparatorIndex = exclude.Length;
					}

					// If this is excluded dir, match without the guid part.
					if (path.StartsWith(exclude.Substring(0, guidSeparatorIndex), StringComparison.OrdinalIgnoreCase))
						return true;

				} else {

					var filename = Path.GetFileName(path);
					if (filename.IndexOf(exclude, StringComparison.OrdinalIgnoreCase) != -1)
						return true;
				}
			}

			return false;
		}

		private static KeyValuePair<TKey, TValue> MakeKVP<TKey, TValue>(TKey key, TValue value)
		{
			return new KeyValuePair<TKey, TValue>(key, value);
		}
	}

	//
	// Monitors scene assets for any modifications and signals to the ScenesViewWindow.
	//
	internal class ScenesInProjectChangeProcessor : UnityEditor.AssetModificationProcessor
	{

		// NOTE: This won't be called when duplicating a scene. Unity says so!
		// More info: https://issuetracker.unity3d.com/issues/assetmodificationprocessor-is-not-notified-when-an-asset-is-duplicated
		// The only way to get notified is via AssetPostprocessor, but that gets called for everything (saving scenes including).
		// Check the implementation below.
		private static void OnWillCreateAsset(string path)
		{
			CheckAndNotifyForPath(path, false);
		}

		private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions option)
		{
			CheckAndNotifyForPath(path);

			return AssetDeleteResult.DidNotDelete;
		}

		private static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
		{
			CheckAndNotifyForPath(oldPath);

			return AssetMoveResult.DidNotMove;
		}

		private static void CheckAndNotifyForPath(string path, bool checkForFolders = true)
		{
			if (path.EndsWith(".unity.meta") || path.EndsWith(".unity")) {
				ScenesInProject.AssetsChanged = true;
				ScenesInProject.RepaintAllInstances();
			} else if (checkForFolders) {
				// Check if this is folder. Folders can contain scenes.
				// This is not accurate, but fast?
				for (int i = path.Length - 1; i >= 0; --i) {
					char ch = path[i];
					if (ch == '.')  // It's a file (hopefully)
						break;

					if (ch == '/') { // It's a folder
						ScenesInProject.AssetsChanged = true;
						ScenesInProject.RepaintAllInstances();
						break;
					}
				}
			}
		}
	}

	// NOTE: This gets called for every asset change including saving scenes.
	// This might have small performance hit for big projects so don't use it.
	//internal class ScenesInProjectAssetPostprocessor : AssetPostprocessor
	//{
	//	private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
	//	{
	//		if (importedAssets.Any(path => path.EndsWith(".unity"))) {
	//			ScenesInProject.AssetsChanged = true;
	//		}
	//	}
	//}

}
