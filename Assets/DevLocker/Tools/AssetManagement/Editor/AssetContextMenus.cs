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
			@"C:\Program Files\Sublime Text 3\subl.exe",
			@"C:\Program Files\Sublime Text 3\sublime_text.exe",
			@"C:\Program Files\Sublime Text 2\sublime_text.exe",
			@"C:\Program Files (x86)\Sublime Text 3\subl.exe",
			@"C:\Program Files (x86)\Sublime Text 3\sublime_text.exe",
			@"C:\Program Files (x86)\Sublime Text 2\sublime_text.exe",
			@"C:\Programs\Sublime Text 3\subl.exe",
			@"C:\Programs\Sublime Text 3\sublime_text.exe",
			@"C:\Programs\Sublime Text 2\sublime_text.exe",
		};

		public const int EditWith_MenuItemPriorityStart = -980;

		#region Text Editing

		[MenuItem("Assets/Edit With/Notepad++", false, EditWith_MenuItemPriorityStart + 0)]
		private static void EditWithNotepadPlusPlus()
			=> EditWithApp("notepad++.exe", GetPathsOfAssetsJoined(Selection.objects, false), _notepadPaths);

		[MenuItem("Assets/Edit With/Notepad++ Metas", false, EditWith_MenuItemPriorityStart + 1)]
		private static void EditWithNotepadPlusPlusMetas()
			=> EditWithApp("notepad++.exe", GetPathsOfAssetsJoined(Selection.objects, true), _notepadPaths);
		
		[MenuItem("Assets/Edit With/Sublime", false, EditWith_MenuItemPriorityStart + 2)]
		private static void EditWithSublime()
			=> EditWithApp("subl.exe", GetPathsOfAssetsJoined(Selection.objects, false), _sublimePaths);
		
		[MenuItem("Assets/Edit With/Sublime Metas", false, EditWith_MenuItemPriorityStart + 3)]
		private static void EditWithSublimeMetas()
			=> EditWithApp("subl.exe", GetPathsOfAssetsJoined(Selection.objects, true), _sublimePaths);
		
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

		[MenuItem("Assets/Edit With/Paint.NET", false, EditWith_MenuItemPriorityStart + 20)]
		private static void EditWithPaintDotNet()
			=> EditWithApp("paintdotnet.exe", GetPathsOfAssetsJoined(Selection.objects, false));
		
		[MenuItem("Assets/Edit With/Krita", false, EditWith_MenuItemPriorityStart + 22)]
		private static void EditWithKrita()
			=> EditWithApp("krita.exe", GetPathsOfAssetsJoined(Selection.objects, false));

		[MenuItem("Assets/Edit With/Photoshop", false, EditWith_MenuItemPriorityStart + 24)]
		private static void EditWithPhotoshop()
		{
			if (TryEditWithApp("Photoshop.exe", GetPathsOfAssetsJoined(Selection.objects, false)))
				return;

			EditWithApp("adbps", GetPathsOfAssetsJoined(Selection.objects, false));
		}

		[MenuItem("Assets/Edit With/Gimp", false, EditWith_MenuItemPriorityStart + 26)]
		private static void EditWithGimp()
			=> EditWithApp("GIMP2.png", GetPathsOfAssetsJoined(Selection.objects, false));


		[MenuItem("Assets/Edit With/Paint.NET", true, EditWith_MenuItemPriorityStart + 20)]
		[MenuItem("Assets/Edit With/Krita", true, EditWith_MenuItemPriorityStart + 22)]
		[MenuItem("Assets/Edit With/Photoshop", true, EditWith_MenuItemPriorityStart + 24)]
		[MenuItem("Assets/Edit With/Gimp", true, EditWith_MenuItemPriorityStart + 26)]
		private static bool EditWithTextureValidate()
			=> Selection.objects.All(o => o is Texture);

		#endregion

		#region Models Editing

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
			EditWithApp("blender.exe", $"--python-expr \"" +    // start python expression.

				$"import bpy;\n" +
				//$"bpy.context.preferences.view.show_splash=False;\n" + // This is persistent so skip it.
				"bpy.ops.scene.new(type='EMPTY');\n" +
				$"bpy.ops.import_scene.fbx(filepath=r'{fullPath}');\n" +

				$"\""   // End python expression
				); // Idea by: https://blog.kikicode.com/2018/12/double-click-fbx-files-to-import-to.html
		}


		[MenuItem("Assets/Edit With/Blender", true, EditWith_MenuItemPriorityStart + 40)]
		private static bool EditWithModelValidate()
			=> Selection.objects.All(o => PrefabUtility.GetPrefabAssetType(o) == PrefabAssetType.Model);

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

		private static void EditWithApp(string appRegistryName, string args, params string[] fallbackPaths)
		{
			if (!TryEditWithApp(appRegistryName, args, fallbackPaths)) {
				EditorUtility.DisplayDialog("Error", $"Program \"{appRegistryName}\" is not found.", "Sad");
				return;
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
				}

				if (File.Exists(executablePath)) {
					System.Diagnostics.Process.Start(executablePath, args);

					return true;
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
