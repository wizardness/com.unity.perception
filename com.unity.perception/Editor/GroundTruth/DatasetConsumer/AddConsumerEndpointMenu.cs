using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.UIElements;

namespace UnityEditor.Perception.GroundTruth.DatasetConsumer
{
    public class AddConsumerEndpointMenu : VisualElement
    {
        const string k_DefaultDirectoryText = "ConsumerEndpoints";
        string m_CurrentPath = string.Empty;
        VisualElement m_DirectoryChevron;
        TextElement m_DirectoryLabelText;
        Dictionary<string, HashSet<string>> m_MenuDirectories = new Dictionary<string, HashSet<string>>();
        VisualElement m_MenuElements;
        List<MenuItem> m_MenuItems = new List<MenuItem>();
        Dictionary<string, List<MenuItem>> m_MenuItemsMap = new Dictionary<string, List<MenuItem>>();

        EndpointList m_EndpointList;

        string m_SearchString = string.Empty;

        public AddConsumerEndpointMenu(VisualElement parentElement, VisualElement button, EndpointList endpointList)
        {
            m_EndpointList = endpointList;
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"{StaticData.uxmlDir}/ConsumerEndpoints/AddConsumerEndpointMenu.uxml");
            template.CloneTree(this);
            style.position = new StyleEnum<Position>(Position.Absolute);

            var buttonPosition = button.worldBound.position;
            var top = math.min(buttonPosition.y, parentElement.worldBound.height - 300);
            style.top = top;
            style.left = buttonPosition.x;

            focusable = true;
            RegisterCallback<FocusOutEvent>(evt =>
            {
                if (evt.relatedTarget == null || ((VisualElement)evt.relatedTarget).FindCommonAncestor(this) != this)
                    ExitMenu();
            });

            var directoryLabel = this.Q<VisualElement>("directory-label");
            directoryLabel.RegisterCallback<MouseUpEvent>(evt => { AscendDirectory(); });
            m_DirectoryLabelText = this.Q<TextElement>("directory-label-text");
            m_DirectoryChevron = this.Q<VisualElement>("directory-chevron");

            var searchBar = this.Q<TextField>("search-bar");
            searchBar.schedule.Execute(() => searchBar.ElementAt(0).Focus());
            searchBar.RegisterValueChangedCallback(evt => searchString = evt.newValue);

            m_MenuElements = this.Q<VisualElement>("menu-options");

            CreateMenuItems();
            DrawDirectoryItems();
        }

        string currentPath
        {
            get => m_CurrentPath;
            set
            {
                m_CurrentPath = value;
                DrawDirectoryItems();
            }
        }

        string currentPathName
        {
            get
            {
                if (m_CurrentPath == string.Empty)
                    return k_DefaultDirectoryText;
                var pathItems = m_CurrentPath.Split('/');
                return pathItems[pathItems.Length - 1];
            }
        }

        string searchString
        {
            get => m_SearchString;
            set
            {
                m_SearchString = value;
                if (m_SearchString == string.Empty)
                    DrawDirectoryItems();
                else
                    DrawSearchItems();
            }
        }

        string directoryText
        {
            set
            {
                m_DirectoryLabelText.text = value;
                m_DirectoryChevron.style.visibility = value == k_DefaultDirectoryText
                    ? new StyleEnum<Visibility>(Visibility.Hidden)
                    : new StyleEnum<Visibility>(Visibility.Visible);
            }
        }

        void ExitMenu()
        {
            parent.Remove(this);
        }

        void AddEndpoint(Type endpointType)
        {
#if false
            m_EndpointList.AddEndpoint(endpointType);
            ExitMenu();
#endif
        }

        void AscendDirectory()
        {
            var pathItems = m_CurrentPath.Split('/');
            var path = pathItems[0];
            for (var i = 1; i < pathItems.Length - 1; i++)
                path = $"{path}/{pathItems[i]}";
            currentPath = path;
        }

        void DrawDirectoryItems()
        {
            directoryText = currentPathName;
            m_MenuElements.Clear();

            if (m_MenuDirectories.ContainsKey(currentPath))
            {
                var directories = m_MenuDirectories[currentPath];
                foreach (var directory in directories)
                    m_MenuElements.Add(new MenuDirectoryElement(directory, this));
            }

            if (m_MenuItemsMap.ContainsKey(currentPath))
            {
                var menuItems = m_MenuItemsMap[currentPath];
                foreach (var menuItem in menuItems)
                    m_MenuElements.Add(new MenuItemElement(menuItem, this));
            }
        }

        void DrawSearchItems()
        {
            directoryText = k_DefaultDirectoryText;
            m_MenuElements.Clear();

            var upperSearchString = searchString.ToUpper();
            foreach (var menuItem in m_MenuItems)
                if (menuItem.itemName.ToUpper().Contains(upperSearchString))
                    m_MenuElements.Add(new MenuItemElement(menuItem, this));
        }

        void CreateMenuItems()
        {
            var rootList = new List<MenuItem>();
            m_MenuItemsMap.Add(string.Empty, rootList);

            var endpointTypeSet = new HashSet<Type>();
#if false
            foreach (var endpoint in m_EndpointList.datasetCapture.consumerEndpoints)
                endpointTypeSet.Add(endpoint.GetType());
#endif
            foreach (var endpointType in StaticData.endpointTypes)
            {
                if (endpointTypeSet.Contains(endpointType))
                    continue;
                var menuAttribute = (AddConsumerEndpointMenuAttribute)Attribute.GetCustomAttribute(
                    endpointType, typeof(AddConsumerEndpointMenuAttribute));
                if (menuAttribute != null)
                {
                    var pathItems = menuAttribute.menuPath.Split('/');
                    if (pathItems.Length > 1)
                    {
                        var path = string.Empty;
                        var itemName = pathItems[pathItems.Length - 1];
                        for (var i = 0; i < pathItems.Length - 1; i++)
                        {
                            var childPath = $"{path}/{pathItems[i]}";
                            if (i < pathItems.Length - 1)
                            {
                                if (!m_MenuDirectories.ContainsKey(path))
                                    m_MenuDirectories.Add(path, new HashSet<string>());
                                m_MenuDirectories[path].Add(childPath);
                            }

                            path = childPath;
                        }

                        if (!m_MenuItemsMap.ContainsKey(path))
                            m_MenuItemsMap.Add(path, new List<MenuItem>());

                        var item = new MenuItem(endpointType, itemName);
                        m_MenuItems.Add(item);
                        m_MenuItemsMap[path].Add(item);
                    }
                    else
                    {
                        if (pathItems.Length == 0)
                            throw new AssertionException("Empty consumer endpoint menu path");
                        var item = new MenuItem(endpointType, pathItems[0]);
                        m_MenuItems.Add(item);
                        rootList.Add(item);
                    }
                }
                else
                {
                    rootList.Add(new MenuItem(endpointType, endpointType.Name));
                }
            }

            m_MenuItems.Sort((item1, item2) => item1.itemName.CompareTo(item2.itemName));
        }

        class MenuItem
        {
            public string itemName;
            public Type endpointType;

            public MenuItem(Type endpointType, string itemName)
            {
                this.endpointType = endpointType;
                this.itemName = itemName;
            }
        }

        sealed class MenuItemElement : TextElement
        {
            public MenuItemElement(MenuItem menuItem, AddConsumerEndpointMenu menu)
            {
                text = menuItem.itemName;
                AddToClassList("consumer-endpoint__add-menu-directory-item");
                RegisterCallback<MouseUpEvent>(evt => menu.AddEndpoint(menuItem.endpointType));
            }
        }

        sealed class MenuDirectoryElement : VisualElement
        {
            public MenuDirectoryElement(string directory, AddConsumerEndpointMenu menu)
            {
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    $"{StaticData.uxmlDir}/ConsumerEndpoint/MenuDirectoryElement.uxml").CloneTree(this);
                var pathItems = directory.Split('/');
                this.Q<TextElement>("directory").text = pathItems[pathItems.Length - 1];
                RegisterCallback<MouseUpEvent>(evt => menu.currentPath = directory);
            }
        }
    }
}
