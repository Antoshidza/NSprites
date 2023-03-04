using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;

namespace NSprites
{
    public class NSpritesWindow : EditorWindow
    {
        [MenuItem("Window/Entities/NSprites")]
        public static void OpenWindow()
        {
            var window = GetWindow<NSpritesWindow>();
            window.titleContent = new GUIContent("NSprites");
        }

        private void OnEnable()
        {
            UpdateNSpritesData();
            EditorApplication.update += UpdateNSpritesData;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateNSpritesData;
        }

        private class Table
        {
            public List<Column> Columns;
            public Color EvenColor;
            public Color OddColor;

            public Table(Color evenColor, Color oddColor)
            {
                Columns = new();
                EvenColor = evenColor;
                OddColor = oddColor;
            }

            public Column CreateColumn()
            {
                var column = new Column(EvenColor, OddColor);
                Columns.Add(column);
                return column;
            }
        }
        private class Column
        {
            public List<VisualElement> Elements;
            public Color EvenColor;
            public Color OddColor;
            
            public Column(Color evenColor, Color oddColor)
            {
                Elements = new List<VisualElement>();
                EvenColor = evenColor;
                OddColor = oddColor;
            }
            public void Add(VisualElement element)
            {
                element.style.backgroundColor = Elements.Count % 2 == 0 ? EvenColor : OddColor;
                
                Elements.Add(element);
                if(Elements.Count == 1)
                    element.RegisterCallback<GeometryChangedEvent>(_ => { ResolveWidth(); });
            }

            public void ResolveWidth()
            {
                var maxWidth = 0f;
                foreach (var element in Elements)
                    if (maxWidth < element.resolvedStyle.width)
                        maxWidth = element.resolvedStyle.width;

                // to prevent handle when element was enabled
                if (maxWidth == 0f)
                    return;

                foreach (var elem in Elements)
                    elem.style.width = new StyleLength(maxWidth);
            }
        }

        private void UpdateNSpritesData()
        {
            var scrollView =  rootVisualElement.Q<ScrollView>("RootScrollView");
            if (scrollView == null)
            {
                scrollView = new ScrollView { name = "RootScrollView" };
                rootVisualElement.Add(scrollView);
            }

            var worlds = World.All;
            TruncateWorlds(worlds, scrollView);
            for (var i = 0; i < worlds.Count; i++)
                UpdateWorld(worlds[i], scrollView);
        }

        private void TruncateWorlds(in World.NoAllocReadOnlyCollection<World> worlds, VisualElement root)
        {
            var children = root.Children();
            for (int childrenIndex = 0; childrenIndex < children.Count(); childrenIndex++)
            {
                var child = children.ElementAt(childrenIndex);
                var present = false;
                for (int i = 0; i < worlds.Count; i++)
                {
                    if (worlds[i].Name == child.name)
                    {
                        present = true;
                        break;
                    }
                }
                if(!present)
                    root.Remove(child);   
            }
        }
        private void UpdateWorld(World world, VisualElement root)
        {
            var systemHandle = world.GetExistingSystem<SpriteRenderingSystem>();

            if (systemHandle == SystemHandle.Null)
                return;

            var worldContainer = root.Q<Foldout>(world.Name);
            RenderArchetypeStorage storage = null;
            if (worldContainer == null)
            {
                worldContainer = new Foldout
                {
                    name = world.Name,
                    text = world.Name
                };
                root.Add(worldContainer);
                storage = world.EntityManager.GetComponentObject<RenderArchetypeStorage>(systemHandle);
                worldContainer.Add(DisplayPropertiesTable(storage));
            }
            
            if(storage == null)
                storage = world.EntityManager.GetComponentObject<RenderArchetypeStorage>(systemHandle);
            
            foreach (var renderArchetype in storage.RenderArchetypes)
                UpdateRenderArchetype(renderArchetype, worldContainer);
        }
        private Foldout DisplayWorld(World world, in SystemHandle systemHandle)
        {
            var renderArchetypeStorage = world.EntityManager.GetComponentObject<RenderArchetypeStorage>(systemHandle);
            var worldContainer = new Foldout
            {
                name = world.Name,
                text = world.Name
            };

            worldContainer.Add(DisplayPropertiesTable(renderArchetypeStorage));

            foreach (var renderArchetype in renderArchetypeStorage.RenderArchetypes)
                worldContainer.Add(DisplayRenderArchetype(renderArchetype));

            return worldContainer;
        }

        private VisualElement DisplayPropertiesTable(RenderArchetypeStorage archetypeStorage)
        {
            var propertiesContainer = new Foldout
            {
                text = "Properties"
            };

            var propertiesTable = new Table(new Color32(100, 100, 100, 255), new Color32(80, 80, 80, 255));
            var typeColumn = propertiesTable.CreateColumn();
            var idColumn = propertiesTable.CreateColumn();
            
            foreach (var prop in archetypeStorage.PropertyMap)
            {
                var container = new VisualElement { style = { flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row) } };
                propertiesContainer.Add(container);
            
                var typeLabel = new Label($"{prop.Value.GetManagedType().Name}");
                var idLabel = new Label($"{prop.Key}") { style = { fontSize = new StyleLength(10) } };
                container.Add(typeLabel);
                container.Add(idLabel);
                typeColumn.Add(typeLabel);
                idColumn.Add(idLabel);
            }

            return propertiesContainer;
        }

        private void UpdateRenderArchetype(RenderArchetype renderArchetype, VisualElement root)
        {
            var renderContainer = root.Q<Foldout>($"RA_{renderArchetype._id}");
            if (renderContainer == null)
            {
                renderContainer = DisplayRenderArchetype(renderArchetype);
                root.Add(renderContainer);
            }

            renderContainer.text = GetRenderArchetypeTitle(renderArchetype);
        }
        private string GetRenderArchetypeTitle(RenderArchetype renderArchetype) =>
            $"Render: {renderArchetype._id} capacity: {renderArchetype._perChunkPropertiesSpaceCounter.count} / {renderArchetype._perChunkPropertiesSpaceCounter.capacity}";
        private Foldout DisplayRenderArchetype(RenderArchetype renderArchetype)
        {
            var renderContainer = new Foldout
            {
                name = $"RA_{renderArchetype._id}", 
                // text = GetRenderArchetypeTitle(renderArchetype), // will be updated in update method
                value = false
            };
            // since render can't be modified we can spawn it's data once without updating
            renderContainer.RegisterValueChangedCallback(foldoutState =>
            {
                // true means opened
                if(renderContainer.childCount == 0 && foldoutState.newValue)
                    renderContainer.Add(DisplayRenderArchetypeContent(renderArchetype));
            });

            return renderContainer;
        }
        private VisualElement DisplayRenderArchetypeContent(RenderArchetype renderArchetype)
        {
            var container = new VisualElement();
            
            var matContainer = new VisualElement();
            matContainer.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
            container.Add(matContainer);
            
            var matField = new ObjectField("material");
            matContainer.Add(matField);
            matField.value = renderArchetype._material;
            matField.SetEnabled(false);
            matField.objectType = typeof(Material);

            var propertyTable = new Table(new Color32(100, 100, 100, 255), new Color32(80, 80, 80, 255));
            var typeColumn = propertyTable.CreateColumn();
            var idColumn = propertyTable.CreateColumn();
            var updateModeColumn = propertyTable.CreateColumn();

            VisualElement CreateRowVisualElement() => new() { style = { flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row) } };

            VisualElement DisplayProperty(InstancedProperty property, PropertyUpdateMode updateMode)
            {
                var propContainer = CreateRowVisualElement();
                
                var typeLabel = new Label(property.ComponentType.GetManagedType().Name);
                var idLabel = new Label(property._propertyID.ToString());
                var updateModeLabel = new Label(updateMode.ToString());

                static Color GetColor(PropertyUpdateMode updateMode)
                {
                    switch (updateMode)
                    {
                        case PropertyUpdateMode.Reactive:
                            return new Color32(59, 115, 178, 255);
                        case PropertyUpdateMode.EachUpdate:
                            return new Color32(230, 147, 22, 255);
                        case PropertyUpdateMode.Static:
                            return new Color32(59, 178, 119, 255);
                        default:
                            return Color.clear;
                    }
                }
                
                propContainer.Add(typeLabel);
                propContainer.Add(idLabel);
                propContainer.Add(updateModeLabel);
                
                typeColumn.Add(typeLabel);
                idColumn.Add(idLabel);
                updateModeColumn.Add(updateModeLabel);
                updateModeLabel.style.backgroundColor = new StyleColor(GetColor(updateMode));

                return propContainer;
            }

            foreach (var prop in renderArchetype.GetPropertiesData())
                container.Add(DisplayProperty(prop.Item1, prop.Item2));
            var propPointerContainer = DisplayProperty(renderArchetype._pointersProperty, PropertyUpdateMode.EachUpdate);
            propPointerContainer.style.opacity = .25f;
            container.Add(propPointerContainer);

            return container;
        }
    }   
}