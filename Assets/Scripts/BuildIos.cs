#if UNITY_EDITOR && UNITY_IOS
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public class BuildIos
{
  [PostProcessBuild]
  public static void OnPostProcessBuild(BuildTarget target, string path)
  {
    var projectPath = PBXProject.GetPBXProjectPath(path);
    var project = new PBXProject();
    project.ReadFromString(File.ReadAllText(projectPath));
#if UNITY_2019_3_OR_NEWER
    var targetGuid = project.GetUnityFrameworkTargetGuid();
#else
    var targetGuid = project.TargetGuidByName(PBXProject.GetUnityTargetName());
#endif

    // libz.tbd for grpc ios build
    project.AddFrameworkToProject(targetGuid, "libz.tbd", false);

    // bitode is disabled for libgrpc_csharp_ext, so need to disable it for the whole project
    project.SetBuildProperty(targetGuid, "ENABLE_BITCODE", "NO");

    File.WriteAllText(projectPath, project.WriteToString());
  }
}
#endif
