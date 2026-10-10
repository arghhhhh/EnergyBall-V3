using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SceneController inspector laid out like the in-game settings menu: fields tagged
/// [SettingGroup] are drawn in collapsible sections (in <see cref="SettingSections.Order"/>),
/// one box per group, in declaration order. A nested settings object whose fields are all tagged
/// (handVfx) is flattened into those groups. Untagged fields (scene references) are drawn on top.
/// Everything else (ShowIf, Label, buttons, ...) works as in NaughtyInspector.
/// </summary>
[CustomEditor(typeof(SceneController))]
[CanEditMultipleObjects]
public class SceneControllerEditor : NaughtyInspector
{
    private const string SectionPrefKeyPrefix = "SceneControllerEditor.SectionExpanded.";

    private List<SerializedProperty> topLevelProperties = new();

    // Attribute lookups by property path (reflection is too slow to repeat every repaint).
    private readonly Dictionary<string, SettingGroupAttribute> groupByPath = new();

    private GUIStyle sectionStyle;

    private class Group
    {
        public string name;
        public readonly List<SerializedProperty> properties = new();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        GetSerializedProperties(ref topLevelProperties);

        var ungrouped = new List<SerializedProperty>();
        var sections = new Dictionary<string, List<Group>>();

        foreach (var property in topLevelProperties)
        {
            var attribute = GetGroup(property);
            if (attribute != null)
            {
                AddToGroup(sections, attribute, property);
                continue;
            }

            var children = GroupedChildren(property);
            if (children != null)
            {
                foreach (var (childAttribute, child) in children)
                    AddToGroup(sections, childAttribute, child);
            }
            else
            {
                ungrouped.Add(property);
            }
        }

        foreach (var property in ungrouped)
        {
            if (property.name == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(property);
            }
            else
            {
                NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
            }
        }

        var sectionOrder = SettingSections.Order.Concat(
            sections.Keys.Where(s => !SettingSections.Order.Contains(s))
        );
        foreach (string section in sectionOrder)
        {
            if (sections.TryGetValue(section, out var groups))
                DrawSection(section, groups);
        }

        serializedObject.ApplyModifiedProperties();

        DrawNonSerializedFields();
        DrawNativeProperties();
        DrawButtons();
    }

    private void DrawSection(string section, List<Group> groups)
    {
        // A section with nothing visible (e.g. Kinect in dummy-only mode) is left out entirely.
        var visibleGroups = groups
            .Select(g =>
                (g.name, properties: g.properties.Where(PropertyUtility.IsVisible).ToList())
            )
            .Where(g => g.properties.Count > 0)
            .ToList();
        if (visibleGroups.Count == 0)
            return;

        // A header bar, so sections stand apart from the bold group titles inside them.
        sectionStyle ??= new GUIStyle(EditorStyles.foldoutHeader)
        {
            fontStyle = FontStyle.Bold,
            fontSize = 14,
            fixedHeight = 24,
        };

        EditorGUILayout.Space(8);
        string prefKey = SectionPrefKeyPrefix + section;
        bool expanded = EditorPrefs.GetBool(prefKey, true);
        bool newExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, section, sectionStyle);
        EditorGUILayout.EndFoldoutHeaderGroup(); // the group boxes below may hold foldouts
        if (newExpanded != expanded)
            EditorPrefs.SetBool(prefKey, newExpanded);
        if (!newExpanded)
            return;

        foreach (var (name, properties) in visibleGroups)
        {
            NaughtyEditorGUI.BeginBoxGroup_Layout(name);
            foreach (var property in properties)
                NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
            NaughtyEditorGUI.EndBoxGroup_Layout();
        }
    }

    private static void AddToGroup(
        Dictionary<string, List<Group>> sections,
        SettingGroupAttribute attribute,
        SerializedProperty property
    )
    {
        if (!sections.TryGetValue(attribute.Section, out var groups))
            sections[attribute.Section] = groups = new List<Group>();

        var group = groups.Find(g => g.name == attribute.Group);
        if (group == null)
            groups.Add(group = new Group { name = attribute.Group });
        group.properties.Add(property);
    }

    private SettingGroupAttribute GetGroup(SerializedProperty property)
    {
        if (!groupByPath.TryGetValue(property.propertyPath, out var attribute))
        {
            attribute = PropertyUtility.GetAttribute<SettingGroupAttribute>(property);
            groupByPath[property.propertyPath] = attribute;
        }
        return attribute;
    }

    /// <summary>
    /// The children of a nested settings object, when every one of them carries [SettingGroup];
    /// otherwise null and the object is drawn whole.
    /// </summary>
    private List<(SettingGroupAttribute, SerializedProperty)> GroupedChildren(
        SerializedProperty property
    )
    {
        if (property.propertyType != SerializedPropertyType.Generic || property.isArray)
            return null;

        var children = new List<(SettingGroupAttribute, SerializedProperty)>();
        var end = property.GetEndProperty();
        var child = property.Copy();
        bool enterChildren = true;
        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            var attribute = GetGroup(child);
            if (attribute == null)
                return null;
            children.Add((attribute, child.Copy()));
        }
        return children.Count > 0 ? children : null;
    }
}
