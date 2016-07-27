﻿//
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
//

using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HoloToolkit.Unity
{
    /// <summary>
    /// Contains utility functions for building for the device
    /// </summary>
    public class BuildDeployTools
    {
        // Consts
        public static readonly string DefaultMSBuildVersion = "14.0";

        // Functions
        public static bool BuildSLN(string buildDirectory, bool showConfDlg = true)
        {
            // Use BuildSLNUtilities to create the SLN
            bool buildSuccess = false;
            // A Strata addition...
            var preBuild = BuildDeployPrefs.PreBuild;
            var postBuild = BuildDeployPrefs.PostBuild;
            // ^^^ cached from the global prefs in case they change
            BuildSLNUtilities.PerformBuild(new BuildSLNUtilities.BuildInfo()
            {
                // These properties should all match what the Standalone.proj file specifies
                OutputDirectory = buildDirectory,
                Scenes = EditorBuildSettings.scenes.Select(scene => scene.path),
                BuildTarget = BuildTarget.WSAPlayer,
                WSASdk = WSASDK.UWP,
                WSAUWPBuildType = WSAUWPBuildType.D3D,

                // A Strata addition...
                PreBuildAction = (buildInfo) => {
                    if (null != preBuild) {
                        preBuild(buildInfo);
                    }
                },

                // Configure a post build action that will compile the generated solution
                PostBuildAction = (buildInfo, buildError) => {
                    // A Strata addition...
                    if (null != postBuild) {
                        buildError = postBuild(buildInfo, buildError);
                    }
                    if (!string.IsNullOrEmpty(buildError))
                    {
                        EditorUtility.DisplayDialog(PlayerSettings.productName + " WindowsStoreApp Build Failed!", buildError, "OK");
                    }
                    else
                    {
                        if (showConfDlg)
                        {
                            EditorUtility.DisplayDialog(PlayerSettings.productName, "Build Complete", "OK");
                        }
                        buildSuccess = true;
                    }
                }
            });

            return buildSuccess;
        }

        public static string CalcMSBuildPath(string msBuildVersion)
        {
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(string.Format(@"Software\Microsoft\MSBuild\ToolsVersions\{0}", msBuildVersion)))
            {
                if (key == null)
                {
                    return null;
                }
                string msBuildBinFolder = key.GetValue("MSBuildToolsPath") as string;
                string msBuildPath = Path.Combine(msBuildBinFolder, "msbuild.exe");
                return msBuildPath;
            }
        }

        public static bool BuildAppxFromSolution(string productName, string msBuildVersion, bool forceRebuildAppx, string buildConfig, string buildDirectory)
        {
            // Get and validate the msBuild path...
            string vs = CalcMSBuildPath(msBuildVersion);
            if (!File.Exists(vs))
            {
                Debug.LogError("MSBuild.exe is missing or invalid (path=" + vs + "). Note that the default version is " + DefaultMSBuildVersion);
                return false;
            }

            // Get the path to the NuGet tool
            string unity = Path.GetDirectoryName(EditorApplication.applicationPath);
            string nugetPath = Path.Combine(unity, @"Data\PlaybackEngines\MetroSupport\Tools\NuGet.exe");
            string storePath = Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), buildDirectory));
            string solutionProjectPath = Path.GetFullPath(Path.Combine(storePath, productName + @".sln"));

            // Before building, need to run a nuget restore to generate a json.lock file. Failing to do
            // this breaks the build in VS RTM
            var nugetPInfo = new System.Diagnostics.ProcessStartInfo();
            nugetPInfo.FileName = nugetPath;
            nugetPInfo.WorkingDirectory = buildDirectory;
            nugetPInfo.UseShellExecute = false;
            nugetPInfo.Arguments = @"restore " + PlayerSettings.productName + "/project.json";
            using (var nugetP = new System.Diagnostics.Process())
            {
                Debug.Log(nugetPath + " " + nugetPInfo.Arguments);
                nugetP.StartInfo = nugetPInfo;
                nugetP.Start();
                nugetP.WaitForExit();
            }

            // Now do the actual build
            var pinfo = new System.Diagnostics.ProcessStartInfo();
            pinfo.FileName = vs;
            pinfo.UseShellExecute = false;
            string buildType = forceRebuildAppx ? "Rebuild" : "Build";
            pinfo.Arguments = string.Format("\"{0}\" /t:{2} /p:Configuration={1} /p:Platform=x86", solutionProjectPath, buildConfig, buildType);
            var p = new System.Diagnostics.Process();

            Debug.Log(vs + " " + pinfo.Arguments);
            p.StartInfo = pinfo;
            p.Start();

            p.WaitForExit();
            if (p.ExitCode == 0)
            {
                Debug.Log("APPX build succeeded!");
            }
            else
            {
                Debug.LogError("MSBuild error (code = " + p.ExitCode + ")");
            }

            if (p.ExitCode != 0)
            {
                EditorUtility.DisplayDialog(PlayerSettings.productName + " build Failed!", "Failed to build appx from solution. Error code: " + p.ExitCode, "OK");
                return false;
            }
            else
            {
                // Build succeeded. Allow user to install build on remote PC
                BuildDeployWindow.OpenWindow();
                return true;
            }
        }
    }
}
