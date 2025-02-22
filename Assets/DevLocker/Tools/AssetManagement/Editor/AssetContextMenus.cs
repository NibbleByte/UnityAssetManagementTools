// MIT License Copyright(c) 2024 Filip Slavov, https://github.com/NibbleByte/UnityAssetManagementTools

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Tools
{
	/// <summary>
	/// Helpful menu items like:
	/// - Copy selected GUIDs/Paths
	/// - Edit With Notepad++, Sublime, etc...
	/// </summary>
	public static class AssetContextMenus
	{
		public const int Copy_MenuItemPriorityStart = -990;

		[MenuItem("Assets/Copy to Clipboard/Copy GUIDs", false, Copy_MenuItemPriorityStart + 0)]
		private static void CopySelectedGuid()
		{
			List<string> guids = new List<string>(Selection.objects.Length);

			foreach (var obj in Selection.objects) {
				string guid;
				long localId;
				if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out guid, out localId))
					continue;

				string resultGuid = guid;
				if (!AssetDatabase.IsMainAsset(obj)) {
					resultGuid =$"{guid}-{localId}";
				}

				guids.Add(resultGuid);
			}

			var result = string.Join("\n", guids);
			Debug.Log($"Guids copied:\n{result}");

			EditorGUIUtility.systemCopyBuffer = result;
		}

		[MenuItem("Assets/Copy to Clipboard/Copy Asset Names", false, Copy_MenuItemPriorityStart + 2)]
		private static void CopySelectedAssetNames()
		{
			// Get by selected objects.
			// Selection.objects my have sub-assets (for example embedded fbx materials).
			// All sub assets have the same guid, but different local id.
			var objectsNames = Selection.objects.Select(o => o.name).ToList();

			// Get by selected guids.
			// Selection.assetGUIDs includes selected folders on the left in two-column project view (Selection.objects does not).
			// Selected sub-assets will return the main asset guid.
			var assetNames = Selection.assetGUIDs
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(Path.GetFileNameWithoutExtension)
				.Where(name => !objectsNames.Contains(name))
				;


			var result = string.Join("\n", objectsNames.Concat(assetNames));
			Debug.Log($"Asset names copied:\n{result}");

			EditorGUIUtility.systemCopyBuffer = result;
		}

		[MenuItem("Assets/Copy to Clipboard/Copy Relative Paths", false, Copy_MenuItemPriorityStart + 3)]
		private static void CopySelectedAssetPaths()
		{
			// Get by selected guids.
			// Selection.assetGUIDs includes selected folders on the left in two-column project view (Selection.objects does not).
			// Selected sub-assets will return the main asset guid.
			var assetNames = Selection.assetGUIDs
				.Select(AssetDatabase.GUIDToAssetPath)
				;

			var result = string.Join("\n", assetNames.Distinct().OrderBy(path => path));
			Debug.Log($"Relative paths copied:\n{result}");

			EditorGUIUtility.systemCopyBuffer = result;
		}

		[MenuItem("Assets/Copy to Clipboard/Copy Absolute Paths", false, Copy_MenuItemPriorityStart + 4)]
		private static void CopySelectedAbsolutePaths()
		{
			var projectRoot = Path.GetDirectoryName(Application.dataPath);

			// Get by selected guids.
			// Selection.assetGUIDs includes selected folders on the left in two-column project view (Selection.objects does not).
			// Selected sub-assets will return the main asset guid.
			var assetNames = Selection.assetGUIDs
				.Select(AssetDatabase.GUIDToAssetPath)
				.Select(Path.GetFullPath)
				;

			var result = string.Join("\n", assetNames.Distinct().OrderBy(path => path));
			Debug.Log($"Absolute paths copied:\n{result}");

			EditorGUIUtility.systemCopyBuffer = result;
		}



#if UNITY_EDITOR_WIN

		private static string[] _notepadPaths = new string[] {
			@"C:\Program Files\Notepad++\notepad++.exe",
			@"C:\Program Files (x86)\Notepad++\notepad++.exe",
			@"C:\Programs\Notepad++\notepad++.exe",
		};
		private static string[] _sublimePaths = new string[] {
			@"c:\Program Files\Sublime Text\sublime_text.exe",
			@"c:\Program Files\Sublime Text\subl.exe",
			@"C:\Program Files\Sublime Text 3\sublime_text.exe",
			@"C:\Program Files\Sublime Text 3\subl.exe",
			@"C:\Program Files\Sublime Text 2\sublime_text.exe",
			@"C:\Program Files\Sublime Text 2\subl.exe",
			@"c:\Program Files (x86)\Sublime Text\sublime_text.exe",
			@"c:\Program Files (x86)\Sublime Text\subl.exe",
			@"C:\Program Files (x86)\Sublime Text 3\sublime_text.exe",
			@"C:\Program Files (x86)\Sublime Text 3\subl.exe",
			@"C:\Program Files (x86)\Sublime Text 2\sublime_text.exe",
			@"C:\Program Files (x86)\Sublime Text 2\subl.exe",
			@"c:\Programs\Sublime Text\sublime_text.exe",
			@"c:\Programs\Sublime Text\subl.exe",
			@"C:\Programs\Sublime Text 3\sublime_text.exe",
			@"C:\Programs\Sublime Text 3\subl.exe",
			@"C:\Programs\Sublime Text 2\sublime_text.exe",
			@"C:\Programs\Sublime Text 2\subl.exe",
		};

		public const int EditWith_MenuItemPriorityStart = -980;

		#region Text Editing

		private const string PrettyKey_NotepadPlusPlus = "Notepad++";
		private const string PrettyKey_Sublime = "Sublime";

		[MenuItem("Assets/Edit With/Notepad++", false, EditWith_MenuItemPriorityStart + 0)]
		private static void EditWithNotepadPlusPlus()
			=> EditWithApp(PrettyKey_NotepadPlusPlus, "notepad++.exe", GetPathsOfAssetsJoined(Selection.objects, false), _notepadPaths);

		[MenuItem("Assets/Edit With/Notepad++ Metas", false, EditWith_MenuItemPriorityStart + 1)]
		private static void EditWithNotepadPlusPlusMetas()
			=> EditWithApp(PrettyKey_NotepadPlusPlus,"notepad++.exe", GetPathsOfAssetsJoined(Selection.objects, true), _notepadPaths);

		[MenuItem("Assets/Edit With/Sublime", false, EditWith_MenuItemPriorityStart + 2)]
		private static void EditWithSublime()
		{
			// Portable versions don't add registry keys for the location.
			// Their folder is also named differently by default. Try to find it in the usual place.
			// C:\Program Files\sublime_text_build_4152_x64\...
			var portablePaths64 = Directory.GetDirectories(@"C:\Program Files\", "sublime_text_build*", SearchOption.TopDirectoryOnly).Select(p => p + @"\sublime_text.exe");
			var portablePaths86 = Directory.GetDirectories(@"C:\Program Files (x86)\", "sublime_text_build*", SearchOption.TopDirectoryOnly).Select(p => p + @"\sublime_text.exe");

			EditWithApp(PrettyKey_Sublime, "sublime_text.exe", GetPathsOfAssetsJoined(Selection.objects, false), _sublimePaths.Concat(portablePaths64).Concat(portablePaths86).ToArray());
		}

		[MenuItem("Assets/Edit With/Sublime Metas", false, EditWith_MenuItemPriorityStart + 3)]
		private static void EditWithSublimeMetas()
		{
			// Portable versions don't add registry keys for the location.
			// Their folder is also named differently by default. Try to find it in the usual place.
			// C:\Program Files\sublime_text_build_4152_x64\...
			var portablePaths64 = Directory.GetDirectories(@"C:\Program Files\", "sublime_text_build*", SearchOption.TopDirectoryOnly).Select(p => p + @"\sublime_text.exe");
			var portablePaths86 = Directory.GetDirectories(@"C:\Program Files (x86)\", "sublime_text_build*", SearchOption.TopDirectoryOnly).Select(p => p + @"\sublime_text.exe");

			EditWithApp(PrettyKey_Sublime, "sublime_text.exe", GetPathsOfAssetsJoined(Selection.objects, true), _sublimePaths.Concat(portablePaths64).Concat(portablePaths86).ToArray());
		}

		[MenuItem("Assets/Edit With/Scripts IDE", false, EditWith_MenuItemPriorityStart + 4)]
		private static void EditWithIDE()
			=> TryEditWithScriptsIDE(GetPathsOfAssetsJoined(Selection.objects, false));

		[MenuItem("Assets/Edit With/Scripts IDE Metas", false, EditWith_MenuItemPriorityStart + 5)]
		private static void EditWithIDEMetas()
			=> TryEditWithScriptsIDE(GetPathsOfAssetsJoined(Selection.objects, true));


		[MenuItem("Assets/Edit With/Notepad++", true, EditWith_MenuItemPriorityStart + 0)]
		[MenuItem("Assets/Edit With/Sublime", true, EditWith_MenuItemPriorityStart + 2)]
		[MenuItem("Assets/Edit With/Scripts IDE", true, EditWith_MenuItemPriorityStart + 4)]
		private static bool EditWithTextValidate()
			=> !EditWithTextureValidate() && !EditWithModelValidate();

		#endregion

		#region Textures Editing

		private const string PrettyKey_PaintDotNet = "Paint.Net";
		private const string PrettyKey_Krita = "Krita";
		private const string PrettyKey_Photoshop = "Photoshop";
		private const string PrettyKey_Gimp = "Gimp";

		[MenuItem("Assets/Edit With/Paint.NET", false, EditWith_MenuItemPriorityStart + 20)]
		private static void EditWithPaintDotNet()
		{
			if (TryEditWithApp("paint.net.1", GetPathsOfAssetsJoined(Selection.objects, false)))
				return;

			EditWithApp(PrettyKey_PaintDotNet, "paintdotnet.exe", GetPathsOfAssetsJoined(Selection.objects, false));
		}

		[MenuItem("Assets/Edit With/Krita", false, EditWith_MenuItemPriorityStart + 22)]
		private static void EditWithKrita()
		{
			if (TryEditWithApp("Krita.Document", GetPathsOfAssetsJoined(Selection.objects, false)))
				return;

			EditWithApp(PrettyKey_Krita, "krita.exe", GetPathsOfAssetsJoined(Selection.objects, false));
		}

		[MenuItem("Assets/Edit With/Photoshop", false, EditWith_MenuItemPriorityStart + 24)]
		private static void EditWithPhotoshop()
		{
			if (TryEditWithApp("Photoshop.exe", GetPathsOfAssetsJoined(Selection.objects, false)))
				return;

			EditWithApp(PrettyKey_Photoshop, "adbps", GetPathsOfAssetsJoined(Selection.objects, false));
		}

		[MenuItem("Assets/Edit With/Gimp", false, EditWith_MenuItemPriorityStart + 26)]
		private static void EditWithGimp()
			=> EditWithApp(PrettyKey_Gimp, "GIMP2.png", GetPathsOfAssetsJoined(Selection.objects, false));


		[MenuItem("Assets/Edit With/Paint.NET", true, EditWith_MenuItemPriorityStart + 20)]
		[MenuItem("Assets/Edit With/Krita", true, EditWith_MenuItemPriorityStart + 22)]
		[MenuItem("Assets/Edit With/Photoshop", true, EditWith_MenuItemPriorityStart + 24)]
		[MenuItem("Assets/Edit With/Gimp", true, EditWith_MenuItemPriorityStart + 26)]
		private static bool EditWithTextureValidate()
			=> Selection.objects.All(o => o is Texture);

		#endregion

		#region Models Editing

		private const string PrettyKey_Blender = "Blender";

		[MenuItem("Assets/Edit With/Blender", false, EditWith_MenuItemPriorityStart + 40)]
		private static void EditWithBlender()
		{
			if (Selection.objects.Length == 0)
				return;

			string fullPath = Path.GetFullPath(AssetDatabase.GetAssetPath(Selection.objects[0]));

			if (fullPath.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase)) {
				AssetDatabase.OpenAsset(Selection.objects[0]);
				return;
			}

			// Assume this is fbx or other model file. It can't be opened directly, it needs to be imported.
			// Idea by: https://blog.kikicode.com/2018/12/double-click-fbx-files-to-import-to.html
			string args = $"--python-expr \"" + // start python expression.

						  $"import bpy;\n" +
						  //$"bpy.context.preferences.view.show_splash=False;\n" + // This is persistent so skip it.
						  "bpy.ops.scene.new(type='EMPTY');\n" +
						  $"bpy.ops.import_scene.fbx(filepath=r'{fullPath}');\n" +

						  $"\"";   // End python expression

			if (TryEditWithApp("blendfile", args))
				return;

			EditWithApp(PrettyKey_Blender, "blender.exe", args);
		}


		[MenuItem("Assets/Edit With/Blender", true, EditWith_MenuItemPriorityStart + 40)]
		private static bool EditWithModelValidate()
			=> Selection.objects.All(o => PrefabUtility.GetPrefabAssetType(o) == PrefabAssetType.Model);

		#endregion

		#region Custom Editing

		private const string PrettyKey_Custom1 = "Custom 1";
		private const string PrettyKey_Custom2 = "Custom 2";
		private const string PrettyKey_Custom3 = "Custom 3";

		[MenuItem("Assets/Edit With/Custom 1", false, EditWith_MenuItemPriorityStart + 60)]
		private static void EditWithCustom1()
			=> EditWithApp(PrettyKey_Custom1, "", GetPathsOfAssetsJoined(Selection.objects, false));

		[MenuItem("Assets/Edit With/Custom 2", false, EditWith_MenuItemPriorityStart + 61)]
		private static void EditWithCustom2()
			=> EditWithApp(PrettyKey_Custom2, "", GetPathsOfAssetsJoined(Selection.objects, false));

		[MenuItem("Assets/Edit With/Custom 3", false, EditWith_MenuItemPriorityStart + 62)]
		private static void EditWithCustom3()
			=> EditWithApp(PrettyKey_Custom3, "", GetPathsOfAssetsJoined(Selection.objects, false));

		#endregion

		#region Editing Utils

		private static string GetPathsOfAssetsJoined(Object[] objects, bool metas, string separator = " ")
		{
			var paths = objects
					.Select(AssetDatabase.GetAssetPath)
					.Where(p => !string.IsNullOrEmpty(p))
					.Select(Path.GetFullPath)
					.Select(p => metas ? AssetDatabase.GetTextMetaFilePathFromAssetPath(p) : p)
					.Select(p => '"' + p + '"')
				;

			return string.Join(separator, paths);
		}

		[MenuItem("Assets/Edit With/Clear Saved App Paths", false, EditWith_MenuItemPriorityStart + 80)]
		private static void ClearSavedAppPaths()
		{
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_NotepadPlusPlus}");
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Sublime}");

			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_PaintDotNet}");
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Krita}");
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Photoshop}");
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Gimp}");

			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Blender}");

			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Custom1}");
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Custom2}");
			EditorPrefs.DeleteKey($"AssetContextMenus_{PrettyKey_Custom3}");
		}

		private static void EditWithApp(string prettyName, string appRegistryName, string args, params string[] fallbackPaths)
		{
			string prefsPath = EditorPrefs.GetString($"AssetContextMenus_{prettyName}");

			if (!string.IsNullOrEmpty(prefsPath) && File.Exists(prefsPath)) {
				System.Array.Resize(ref fallbackPaths, fallbackPaths.Length + 1);
				fallbackPaths[fallbackPaths.Length - 1] = prefsPath;
			}

			if (!TryEditWithApp(appRegistryName, args, fallbackPaths)) {

				if (string.IsNullOrWhiteSpace(prefsPath) || !File.Exists(prefsPath)) {
					prefsPath = EditorUtility.OpenFilePanel($"Locate {prettyName} Executable", "C:\\", "exe");

					if (!string.IsNullOrEmpty(prefsPath) && File.Exists(prefsPath)) {
						EditorPrefs.SetString($"AssetContextMenus_{prettyName}", prefsPath);

						if (TryEditWithApp(appRegistryName, args, prefsPath))
							return;
					}
				}

				EditorUtility.DisplayDialog("Error", $"Program \"{prettyName}\" is not found.", "Sad!");
			}
		}

		public static bool TryEditWithScriptsIDE(string args)
		{
			// This works only for supported files by the IDE (.cs, .txt, etc)
			if (CodeEditor.CurrentEditor.OpenProject(args.Replace("\"", "")))
				return true;


			string ide = CodeEditor.CurrentEditorPath;
			if (string.IsNullOrEmpty(ide) || !File.Exists(ide))
				return false;

			//DirectoryInfo projectDir = Directory.GetParent(Application.dataPath);
			//string solution = Path.Combine(projectDir.FullName, projectDir.Name + ".sln");
			//System.Diagnostics.Process.Start(ide, $"\"{solution}\" {args}");

			// This will open the file in a standalone instance of the IDE.
			// Making it open in the already open instance is too hard as each IDE has it's own implementation and specifics of doing so.
			// For more info check the CodeEditor.CurrentEditor.OpenProject() implementations for each type of IDE.
			System.Diagnostics.Process.Start(ide, $"{args}");
			return true;
		}

		public static bool TryEditWithApp(string appRegistryName, string args, params string[] fallbackPaths)
		{
			if (!string.IsNullOrWhiteSpace(appRegistryName)) {

				const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{0}";
				const string shellCommands = @"SOFTWARE\Classes\{0}\shell\open\command";
				const string shellCommands2 = @"SOFTWARE\Classes\Applications\{0}\shell\open\command";

				// https://stackoverflow.com/a/909966/4612666
				Microsoft.Win32.RegistryKey fileKey = null
					?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(string.Format(appPaths, appRegistryName))
					?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(string.Format(appPaths, appRegistryName))

					?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(string.Format(shellCommands, appRegistryName))
					?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(string.Format(shellCommands, appRegistryName))

					?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(string.Format(shellCommands2, appRegistryName))
					?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(string.Format(shellCommands2, appRegistryName))
					;

				if (fileKey != null) {
					string executablePath = (string)fileKey.GetValue(string.Empty);
					fileKey.Close();

					// Values coming from the shellCommands are in the format:
					// "C:\Programs\Krita\bin\krita.exe" "%1"
					if (executablePath.StartsWith('"')) {
						executablePath = executablePath.Substring(1, executablePath.IndexOf('"', 1) - 1);
					} else {
						// Sometimes executable path is not surrounded in quotes. Try to find the "%1" part.
						int endIndex = executablePath.IndexOf('"');
						if (endIndex == -1) {
							endIndex = executablePath.IndexOf('%');
						}
						if (endIndex != -1) {
							executablePath = executablePath.Substring(0, endIndex - 1).Trim();
						}
					}

					if (File.Exists(executablePath)) {
						System.Diagnostics.Process.Start(executablePath, args);

						return true;
					}
				}
			}

			foreach (string fallbackPath in fallbackPaths) {
				if (File.Exists(fallbackPath)) {
					System.Diagnostics.Process.Start(fallbackPath, args);
					return true;
				}
			}

			return false;
		}

		#endregion

#endif
	}
}
