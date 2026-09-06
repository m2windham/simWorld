using System;
using System.Collections.Generic;
using System.Reflection;

namespace SimWorld.Defs
{
    /// <summary>
    /// Binds the static fields of <see cref="DefOfAttribute"/> classes to loaded Defs by name
    /// (RimWorld: <c>RimWorld.DefOfHelper</c>), so code can write <c>ThingDefOf.Wall</c> instead of a lookup.
    /// </summary>
    public static class DefOfHelper
    {
        /// <summary>Binds every <see cref="DefOfAttribute"/> class in the resolver's assemblies.</summary>
        public static void RebindAllDefOfs(DefDatabase database, DefTypeResolver types, List<DefLoadError> errors)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            foreach (Type type in types.AllTypesWithAttribute<DefOfAttribute>())
            {
                BindDefsFor(type, database, errors);
            }
        }

        /// <summary>Binds one <see cref="DefOfAttribute"/> class. Missing Defs and non-Def fields are errors.</summary>
        public static void BindDefsFor(Type defOfType, DefDatabase database, List<DefLoadError> errors)
        {
            if (defOfType == null) throw new ArgumentNullException(nameof(defOfType));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            foreach (FieldInfo field in defOfType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsInitOnly || field.IsLiteral)
                {
                    continue;
                }
                if (!typeof(Def).IsAssignableFrom(field.FieldType))
                {
                    errors.Add(new DefLoadError("DefOf field " + defOfType.Name + "." + field.Name + " is not a Def type."));
                    continue;
                }
                string defName = field.GetCustomAttribute<DefAliasAttribute>()?.DefName ?? field.Name;
                Def? def = database.GetNamed(field.FieldType, defName);
                if (def == null)
                {
                    // Report and leave the field as it is: a partial content set (tests, tools) must not
                    // unbind what a full load already wired.
                    errors.Add(new DefLoadError("DefOf: no " + field.FieldType.Name + " named '" + defName + "' for " + defOfType.Name + "." + field.Name + "."));
                    continue;
                }
                field.SetValue(null, def);
            }
        }
    }
}
