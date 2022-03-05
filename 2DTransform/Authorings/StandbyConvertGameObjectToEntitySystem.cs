using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace NSprites
{
    public class StandbyConvertGameObjectToEntitySystem : GameObjectConversionSystem
    {
        void Convert(Component component, List<IConvertGameObjectToEntity> convertibles)
        {
            try
            {
                component.GetComponents(convertibles);

                foreach(var convertible in convertibles)
                {
                    var behaviour = convertible as Behaviour;
                    if(behaviour != null && !behaviour.enabled)
                        continue;
#if UNITY_EDITOR
                    if(!ShouldRunConversionSystem(convertible.GetType()))
                        continue;
#endif
                    convertible.Convert(GetPrimaryEntity((Component)convertible), DstEntityManager, this);
                }
            }
            catch(Exception x)
            {
                Debug.LogException(x, component);
            }
        }

        protected override void OnUpdate()
        {
            var convertibles = new List<IConvertGameObjectToEntity>();

            Entities.WithNone<Transform>().ForEach((ConvertPointer ponter) => Convert(ponter.transform, convertibles));
            convertibles.Clear();
        }
    }
}