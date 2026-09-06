using System;
using System.Reflection;

namespace Weave.Runtime
{
    public static class SerializedFieldUtility
    {
        public static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Could not find serialized field '{fieldName}' on {target.GetType().Name}.");
            }

            field.SetValue(target, value);
        }
    }
}
