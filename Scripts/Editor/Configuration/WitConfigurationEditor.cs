﻿/*
 * Copyright (c) Facebook, Inc. and its affiliates.
 *
 * This source code is licensed under the license found in the
 * LICENSE file in the root directory of this source tree.
 */

using System.Collections.Generic;
using System.Linq;
using com.facebook.witai.data;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WitConfiguration))]
public class WitConfigurationEditor : Editor
{
    private WitConfiguration configuration;

    private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();

    private int selectedToolPanel;

    private readonly string[] toolPanelNames = new[]
    {
        "Application",
        "Intents",
        "Entities"
    };

    private readonly string[] toolPanelNamesWithoutAppInfo = new[]
    {
        "Intents",
        "Entities"
    };

    private const int TOOL_PANEL_APP = 0;
    private const int TOOL_PANEL_INTENTS = 1;
    private const int TOOL_PANEL_ENTITIES = 2;

    private Editor applicationEditor;
    private Vector2 scroll;
    private bool appConfigurationFoldout;

    private bool IsTokenValid => !string.IsNullOrEmpty(configuration.clientAccessToken) &&
                                 configuration.clientAccessToken.Length == 32;

    public void OnEnable()
    {
        configuration = target as WitConfiguration;
        configuration.UpdateData();
    }

    public override void OnInspectorGUI()
    {
        configuration = target as WitConfiguration;

        GUILayout.BeginVertical(EditorStyles.helpBox);

        GUILayout.BeginHorizontal();
        appConfigurationFoldout = EditorGUILayout.Foldout(appConfigurationFoldout,
            "Application Configuration");
        if (!string.IsNullOrEmpty(configuration?.application?.name))
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(configuration?.application?.name);
        }

        GUILayout.EndHorizontal();

        if (appConfigurationFoldout || !IsTokenValid)
        {
            GUILayout.BeginHorizontal();
            var token = EditorGUILayout.PasswordField("Client Access Token",
                configuration.clientAccessToken);
            if (token != configuration.clientAccessToken)
            {
                configuration.clientAccessToken = token;

                if (token.Length == 32)
                {
                    configuration.UpdateData();
                }

                EditorUtility.SetDirty(configuration);
            }

            GUILayout.EndHorizontal();


            if (GUILayout.Button("Get Configuration from IDE Token"))
            {
                configuration.clientAccessToken = WitAuthUtility.ClientToken;
                if (WitAuthUtility.AppId != configuration.application.id)
                {
                    configuration.application = new WitApplication()
                    {
                        id = WitAuthUtility.AppId,
                        witConfiguration = configuration
                    };
                }
                configuration.UpdateData(() =>
                {
                    Repaint();
                });
                appConfigurationFoldout = false;
            }
        }

        GUILayout.EndVertical();

        bool hasApplicationInfo = null != configuration?.application;

        if (hasApplicationInfo)
        {
            selectedToolPanel = GUILayout.Toolbar(selectedToolPanel, toolPanelNames);
        }
        else
        {
            selectedToolPanel = GUILayout.Toolbar(selectedToolPanel, toolPanelNamesWithoutAppInfo);
        }

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        switch (hasApplicationInfo ? selectedToolPanel : selectedToolPanel + 1)
        {
            case TOOL_PANEL_APP:
                DrawApplication(configuration.application);
                break;
            case TOOL_PANEL_INTENTS:
                DrawIntents();
                break;
            case TOOL_PANEL_ENTITIES:
                DrawEntities();
                break;
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button("Open Wit.ai"))
        {
            if (!string.IsNullOrEmpty(configuration.application?.id))
            {
                Application.OpenURL($"https://wit.ai/apps/{configuration.application.id}");
            }
            else
            {
                Application.OpenURL("https://wit.ai");
            }
        }
    }

    private void DrawEntities()
    {
        BeginIndent();
        for (int i = 0; i < configuration.entities.Length; i++)
        {
            var entity = configuration.entities[i];
            if (null != entity && Foldout("e:", entity.name))
            {
                DrawEntity(entity);
            }
        }
        EndIndent();
    }

    private void DrawEntity(WitEntity entity)
    {
        InfoField("ID", entity.id);
        if (entity.roles.Length > 0)
        {
            EditorGUILayout.Popup("Roles", 0, entity.roles);
        }

        if (entity.lookups.Length > 0)
        {
            EditorGUILayout.Popup("Lookups", 0, entity.lookups);
        }
    }

    private void DrawIntents()
    {
        BeginIndent();
        for (int i = 0; i < configuration.intents.Length; i++)
        {
            var intent = configuration.intents[i];
            if (null != intent && Foldout("i:", intent.name))
            {
                DrawIntent(intent);
            }
        }
        EndIndent();
    }

    private void DrawIntent(WitIntent intent)
    {
        InfoField("ID", intent.id);
        if (intent.entities.Length > 0)
        {
            var entityNames = intent.entities.Select(e => e.name).ToArray();
            EditorGUILayout.Popup("Entities", 0, entityNames);
        }
    }

    private void DrawApplication(WitApplication application)
    {
        if (string.IsNullOrEmpty(application.name))
        {
            GUILayout.Label("Loading...");
        }
        else
        {
            InfoField("Name", application.name);
            InfoField("ID", application.id);
            InfoField("Language", application.lang);
            InfoField("Created", application.createdAt);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Private", GUILayout.Width(100));
            GUILayout.Toggle(application.isPrivate, "");
            GUILayout.EndHorizontal();
        }
    }

    #region UI Components
    private void BeginIndent()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        GUILayout.BeginVertical();
    }

    private void EndIndent()
    {
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void InfoField(string name, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name, GUILayout.Width(100));
        GUILayout.Label(value, "TextField");
        GUILayout.EndHorizontal();
    }

    private bool Foldout(string keybase, string name)
    {
        string key = keybase + name;
        bool show = false;
        if (!foldouts.TryGetValue(key, out show))
        {
            foldouts[key] = false;
        }

        show = EditorGUILayout.Foldout(show, name, true);
        foldouts[key] = show;
        return show;
    }
    #endregion
}
