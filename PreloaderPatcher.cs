using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KoH2.LargerText.Preloader
{
    /// <summary>
    /// BepInEx preloader patcher. Assembly-CSharp is modified in memory only;
    /// the original DLL on disk is never written by this code.
    /// </summary>
    public static class Patcher
    {
        private const float TextScale = 1.25f;
        private const float InverseTextScale = 1f / TextScale;
        private const string MarkerFieldName = "__koh2LargerTextApplied";

        public static IEnumerable<string> TargetDLLs
        {
            get { yield return "Assembly-CSharp.dll"; }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            ModuleDefinition module = assembly.MainModule;
            TypeDefinition uiText = module.Types.FirstOrDefault(type => type.Name == "UIText");
            if (uiText == null)
                throw new InvalidOperationException("UIText type was not found in Assembly-CSharp.dll.");

            if (uiText.Fields.Any(field => field.Name == MarkerFieldName))
                return;

            MethodDefinition onEnable = uiText.Methods.FirstOrDefault(
                method => method.Name == "OnEnable" && !method.IsStatic && method.Parameters.Count == 0);
            MethodDefinition getTextField = uiText.Methods.FirstOrDefault(
                method => method.Name == "get_text_field" && !method.IsStatic && method.Parameters.Count == 0);

            if (onEnable == null || !onEnable.HasBody)
                throw new InvalidOperationException("UIText.OnEnable method was not found.");
            if (getTextField == null)
                throw new InvalidOperationException("UIText.text_field getter was not found.");

            Instruction anchor = onEnable.Body.Instructions.FirstOrDefault(instruction =>
            {
                var method = instruction.Operand as MethodReference;
                return method != null && method.Name == "UpdateLocalziation";
            });

            if (anchor == null)
                throw new InvalidOperationException("UIText.OnEnable injection point was not found.");

            TypeDefinition messageWnd = module.Types.FirstOrDefault(type => type.Name == "MessageWnd");
            TypeDefinition audienceWindow = module.Types.FirstOrDefault(type => type.Name == "AudienceWindow");
            if (messageWnd == null || audienceWindow == null)
                throw new InvalidOperationException("Dialog window types were not found.");

            MethodReference getMessageWndInParent = GetComponentInParent(module, messageWnd);
            MethodReference getAudienceWindowInParent = GetComponentInParent(module, audienceWindow);

            var markerField = new FieldDefinition(
                MarkerFieldName,
                FieldAttributes.Private,
                module.TypeSystem.Boolean);
            uiText.Fields.Add(markerField);

            TypeReference tmpTextType = module.ImportReference(getTextField.ReturnType);
            MethodReference getFontSize = PropertyGetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference setFontSize = PropertySetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference getAutoSizing = PropertyGetter(module, tmpTextType, "enableAutoSizing", module.TypeSystem.Boolean);
            MethodReference getFontSizeMin = PropertyGetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference setFontSizeMin = PropertySetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference getFontSizeMax = PropertyGetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);
            MethodReference setFontSizeMax = PropertySetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);

            ILProcessor il = onEnable.Body.GetILProcessor();

            // Dialog and audience layouts have fixed-size button containers.
            // Keep every UIText inside them at the game's stock size.
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Call, getMessageWndInParent);
            EmitBefore(il, anchor, OpCodes.Brtrue, anchor);
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Call, getAudienceWindowInParent);
            EmitBefore(il, anchor, OpCodes.Brtrue, anchor);

            // if (__koh2LargerTextApplied) goto original code;
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, markerField);
            EmitBefore(il, anchor, OpCodes.Brtrue, anchor);

            // __koh2LargerTextApplied = true;
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldc_I4_1);
            EmitBefore(il, anchor, OpCodes.Stfld, markerField);

            // if (text_field == null) goto original code;
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Call, getTextField);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);

            // text_field.fontSize = text_field.fontSize * 1.25f;
            EmitScaleProperty(il, anchor, getTextField, getFontSize, setFontSize);

            // Autosizing uses its own limits, so scale those as well.
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Call, getTextField);
            EmitBefore(il, anchor, OpCodes.Callvirt, getAutoSizing);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);
            EmitScaleProperty(il, anchor, getTextField, getFontSizeMin, setFontSizeMin);
            EmitScaleProperty(il, anchor, getTextField, getFontSizeMax, setFontSizeMax);

            RestoreCompactResourceBarText(module);
            RestoreKingdomFoodText(module);
            RestoreAdvantagesVictoryLabel(module);
        }

        private static MethodReference GetComponentInParent(
            ModuleDefinition module,
            TypeReference componentType)
        {
            GenericInstanceMethod existingCall = module.Types
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Select(instruction => instruction.Operand)
                .OfType<GenericInstanceMethod>()
                .FirstOrDefault(method =>
                    method.Name == "GetComponentInParent" &&
                    method.ElementMethod.DeclaringType.FullName == "UnityEngine.Component");

            if (existingCall == null)
                throw new InvalidOperationException("Component.GetComponentInParent<T> reference was not found.");

            var result = new GenericInstanceMethod(module.ImportReference(existingCall.ElementMethod));
            result.GenericArguments.Add(module.ImportReference(componentType));
            return module.ImportReference(result);
        }

        /// <summary>
        /// Piety uses a narrower slot than the other resources in the top bar.
        /// Keep only its displayed number at the stock size; its tooltip remains enlarged.
        /// </summary>
        private static void RestoreCompactResourceBarText(ModuleDefinition module)
        {
            TypeDefinition slotType = module.Types.FirstOrDefault(type => type.Name == "ResourceBarSlot");
            if (slotType == null)
                throw new InvalidOperationException("ResourceBarSlot type was not found.");

            MethodDefinition start = slotType.Methods.FirstOrDefault(
                method => method.Name == "Start" && !method.IsStatic && method.Parameters.Count == 0);
            FieldDefinition resource = slotType.Fields.FirstOrDefault(field => field.Name == "resource");
            FieldDefinition text = slotType.Fields.FirstOrDefault(field => field.Name == "text");

            if (start == null || !start.HasBody || resource == null || text == null)
                throw new InvalidOperationException("ResourceBarSlot.Start fields were not found.");

            Instruction anchor = start.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret);
            if (anchor == null)
                throw new InvalidOperationException("ResourceBarSlot.Start injection point was not found.");

            TypeReference tmpTextType = module.ImportReference(text.FieldType);
            MethodReference getFontSize = PropertyGetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference setFontSize = PropertySetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference getAutoSizing = PropertyGetter(module, tmpTextType, "enableAutoSizing", module.TypeSystem.Boolean);
            MethodReference getFontSizeMin = PropertyGetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference setFontSizeMin = PropertySetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference getFontSizeMax = PropertyGetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);
            MethodReference setFontSizeMax = PropertySetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);

            ILProcessor il = start.Body.GetILProcessor();
            Instruction restore = il.Create(OpCodes.Nop);

            // if (resource != Piety) return;
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, resource);
            EmitBefore(il, anchor, OpCodes.Ldc_I4_5);
            EmitBefore(il, anchor, OpCodes.Bne_Un, anchor);
            il.InsertBefore(anchor, restore);

            // if (text == null) return;
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, text);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);

            EmitScaleFieldProperty(il, anchor, text, getFontSize, setFontSize, InverseTextScale);

            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, text);
            EmitBefore(il, anchor, OpCodes.Callvirt, getAutoSizing);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);
            EmitScaleFieldProperty(il, anchor, text, getFontSizeMin, setFontSizeMin, InverseTextScale);
            EmitScaleFieldProperty(il, anchor, text, getFontSizeMax, setFontSizeMax, InverseTextScale);
        }

        /// <summary>
        /// The kingdom food income in the top bar is controlled by UIKingdomFood,
        /// not ResourceBarSlot. Restore its compact number while leaving the
        /// KingdomFoodIncome tooltip enlarged.
        /// </summary>
        private static void RestoreKingdomFoodText(ModuleDefinition module)
        {
            TypeDefinition foodType = module.Types.FirstOrDefault(type => type.Name == "UIKingdomFood");
            if (foodType == null)
                throw new InvalidOperationException("UIKingdomFood type was not found.");

            MethodDefinition start = foodType.Methods.FirstOrDefault(
                method => method.Name == "Start" && !method.IsStatic && method.Parameters.Count == 0);
            FieldDefinition text = foodType.Fields.FirstOrDefault(field => field.Name == "m_FoodValue");

            if (start == null || !start.HasBody || text == null)
                throw new InvalidOperationException("UIKingdomFood.Start fields were not found.");

            Instruction anchor = start.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret);
            if (anchor == null)
                throw new InvalidOperationException("UIKingdomFood.Start injection point was not found.");

            TypeReference tmpTextType = module.ImportReference(text.FieldType);
            MethodReference getFontSize = PropertyGetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference setFontSize = PropertySetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference getAutoSizing = PropertyGetter(module, tmpTextType, "enableAutoSizing", module.TypeSystem.Boolean);
            MethodReference getFontSizeMin = PropertyGetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference setFontSizeMin = PropertySetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference getFontSizeMax = PropertyGetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);
            MethodReference setFontSizeMax = PropertySetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);

            ILProcessor il = start.Body.GetILProcessor();

            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, text);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);

            EmitScaleFieldProperty(il, anchor, text, getFontSize, setFontSize, InverseTextScale);

            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, text);
            EmitBefore(il, anchor, OpCodes.Callvirt, getAutoSizing);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);
            EmitScaleFieldProperty(il, anchor, text, getFontSizeMin, setFontSizeMin, InverseTextScale);
            EmitScaleFieldProperty(il, anchor, text, getFontSizeMax, setFontSizeMax, InverseTextScale);
        }

        /// <summary>
        /// The Claim Victory button has a fixed-height label container. Restore
        /// only that label after UIKingdomAdvantagesWindow resolves its fields.
        /// </summary>
        private static void RestoreAdvantagesVictoryLabel(ModuleDefinition module)
        {
            TypeDefinition windowType = module.Types.FirstOrDefault(
                type => type.Name == "UIKingdomAdvantagesWindow");
            if (windowType == null)
                throw new InvalidOperationException("UIKingdomAdvantagesWindow type was not found.");

            MethodDefinition init = windowType.Methods.FirstOrDefault(
                method => method.Name == "Init" && !method.IsStatic && method.Parameters.Count == 0);
            FieldDefinition initialized = windowType.Fields.FirstOrDefault(
                field => field.Name == "m_Initiazlied");
            FieldDefinition text = windowType.Fields.FirstOrDefault(
                field => field.Name == "m_VictoryLabel");

            if (init == null || !init.HasBody || initialized == null || text == null)
                throw new InvalidOperationException("UIKingdomAdvantagesWindow.Init fields were not found.");

            Instruction initializedStore = init.Body.Instructions.FirstOrDefault(instruction =>
                instruction.OpCode == OpCodes.Stfld && instruction.Operand == initialized);
            Instruction anchor = initializedStore?.Next;
            if (anchor == null)
                throw new InvalidOperationException("UIKingdomAdvantagesWindow.Init injection point was not found.");

            TypeReference tmpTextType = module.ImportReference(text.FieldType);
            MethodReference getFontSize = PropertyGetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference setFontSize = PropertySetter(module, tmpTextType, "fontSize", module.TypeSystem.Single);
            MethodReference getAutoSizing = PropertyGetter(module, tmpTextType, "enableAutoSizing", module.TypeSystem.Boolean);
            MethodReference getFontSizeMin = PropertyGetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference setFontSizeMin = PropertySetter(module, tmpTextType, "fontSizeMin", module.TypeSystem.Single);
            MethodReference getFontSizeMax = PropertyGetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);
            MethodReference setFontSizeMax = PropertySetter(module, tmpTextType, "fontSizeMax", module.TypeSystem.Single);

            ILProcessor il = init.Body.GetILProcessor();

            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, text);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);

            EmitScaleFieldProperty(il, anchor, text, getFontSize, setFontSize, InverseTextScale);

            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, text);
            EmitBefore(il, anchor, OpCodes.Callvirt, getAutoSizing);
            EmitBefore(il, anchor, OpCodes.Brfalse, anchor);
            EmitScaleFieldProperty(il, anchor, text, getFontSizeMin, setFontSizeMin, InverseTextScale);
            EmitScaleFieldProperty(il, anchor, text, getFontSizeMax, setFontSizeMax, InverseTextScale);
        }

        private static MethodReference PropertyGetter(
            ModuleDefinition module,
            TypeReference declaringType,
            string propertyName,
            TypeReference returnType)
        {
            return module.ImportReference(new MethodReference(
                "get_" + propertyName,
                returnType,
                declaringType)
            {
                HasThis = true
            });
        }

        private static MethodReference PropertySetter(
            ModuleDefinition module,
            TypeReference declaringType,
            string propertyName,
            TypeReference valueType)
        {
            var setter = new MethodReference(
                "set_" + propertyName,
                module.TypeSystem.Void,
                declaringType)
            {
                HasThis = true
            };
            setter.Parameters.Add(new ParameterDefinition(valueType));
            return module.ImportReference(setter);
        }

        private static void EmitScaleProperty(
            ILProcessor il,
            Instruction anchor,
            MethodReference getTextField,
            MethodReference getter,
            MethodReference setter)
        {
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Call, getTextField);
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Call, getTextField);
            EmitBefore(il, anchor, OpCodes.Callvirt, getter);
            EmitBefore(il, anchor, OpCodes.Ldc_R4, TextScale);
            EmitBefore(il, anchor, OpCodes.Mul);
            EmitBefore(il, anchor, OpCodes.Callvirt, setter);
        }

        private static void EmitScaleFieldProperty(
            ILProcessor il,
            Instruction anchor,
            FieldReference textField,
            MethodReference getter,
            MethodReference setter,
            float scale)
        {
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, textField);
            EmitBefore(il, anchor, OpCodes.Ldarg_0);
            EmitBefore(il, anchor, OpCodes.Ldfld, textField);
            EmitBefore(il, anchor, OpCodes.Callvirt, getter);
            EmitBefore(il, anchor, OpCodes.Ldc_R4, scale);
            EmitBefore(il, anchor, OpCodes.Mul);
            EmitBefore(il, anchor, OpCodes.Callvirt, setter);
        }

        private static void EmitBefore(ILProcessor il, Instruction anchor, OpCode opcode)
        {
            il.InsertBefore(anchor, il.Create(opcode));
        }

        private static void EmitBefore(ILProcessor il, Instruction anchor, OpCode opcode, FieldReference field)
        {
            il.InsertBefore(anchor, il.Create(opcode, field));
        }

        private static void EmitBefore(ILProcessor il, Instruction anchor, OpCode opcode, MethodReference method)
        {
            il.InsertBefore(anchor, il.Create(opcode, method));
        }

        private static void EmitBefore(ILProcessor il, Instruction anchor, OpCode opcode, Instruction target)
        {
            il.InsertBefore(anchor, il.Create(opcode, target));
        }

        private static void EmitBefore(ILProcessor il, Instruction anchor, OpCode opcode, float value)
        {
            il.InsertBefore(anchor, il.Create(opcode, value));
        }
    }
}
