﻿using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Mvc.ModelBinding.Internal;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ModelBindingPro.Binders
{
    public class CustomDictionaryModelBinder<TKey, TValue> : CustomCollectionModelBinder<KeyValuePair<TKey, TValue>>
    {
        private readonly IModelBinder _valueBinder;


        /// <summary>
        /// Creates a new <see cref="DictionaryModelBinder{TKey, TValue}"/>.
        /// </summary>
        /// <param name="keyBinder">The <see cref="IModelBinder"/> for <typeparamref name="TKey"/>.</param>
        /// <param name="valueBinder">The <see cref="IModelBinder"/> for <typeparamref name="TValue"/>.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
        public CustomDictionaryModelBinder(IModelBinder keyBinder, IModelBinder valueBinder, ILoggerFactory loggerFactory)
            : base(new KeyValuePairModelBinder<TKey, TValue>(keyBinder, valueBinder, loggerFactory), loggerFactory)
        {
            if (valueBinder == null)
            {
                throw new ArgumentNullException(nameof(valueBinder));
            }

            _valueBinder = valueBinder;
        }

        /// <inheritdoc />
        public override async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext == null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            await base.BindModelAsync(bindingContext);
            if (!bindingContext.Result.IsModelSet)
            {
                // No match for the prefix at all.
                return;
            }

            var result = bindingContext.Result;

            Debug.Assert(result.Model != null);
            var model = (IDictionary<TKey, TValue>)result.Model;
            if (model.Count != 0)
            {
                // ICollection<KeyValuePair<TKey, TValue>> approach was successful.
                return;
            }

            //Logger.NoKeyValueFormatForDictionaryModelBinder(bindingContext);

            if (!(bindingContext.ValueProvider is IEnumerableValueProvider enumerableValueProvider))
            {
                // No IEnumerableValueProvider available for the fallback approach. For example the user may have
                // replaced the ValueProvider with something other than a CompositeValueProvider.
                return;
            }

            // Attempt to bind dictionary from a set of prefix[key]=value entries. Get the short and long keys first.
            var prefix = bindingContext.ModelName;
            var keys = enumerableValueProvider.GetKeysFromPrefix(prefix);
            if (keys.Count == 0)
            {
                // No entries with the expected keys.
                return;
            }

            // Update the existing successful but empty ModelBindingResult.
            var elementMetadata = bindingContext.ModelMetadata.ElementMetadata;
            var valueMetadata = elementMetadata.Properties[nameof(KeyValuePair<TKey, TValue>.Value)];

            var keyMappings = new Dictionary<string, TKey>(StringComparer.Ordinal);
            foreach (var kvp in keys)
            {
                // Use InvariantCulture to convert the key since ExpressionHelper.GetExpressionText() would use
                // that culture when rendering a form.
                var convertedKey = ModelBindingHelper.ConvertTo<TKey>(kvp.Key, culture: null);

                using (bindingContext.EnterNestedScope(
                    modelMetadata: valueMetadata,
                    fieldName: bindingContext.FieldName,
                    modelName: kvp.Value,
                    model: null))
                {
                    await _valueBinder.BindModelAsync(bindingContext);

                    var valueResult = bindingContext.Result;
                    if (!valueResult.IsModelSet)
                    {
                        // Factories for IKeyRewriterValueProvider implementations are not all-or-nothing i.e.
                        // "[key][propertyName]" may be rewritten as ".key.propertyName" or "[key].propertyName". Try
                        // again in case this scope is binding a complex type and rewriting
                        // landed on ".key.propertyName" or in case this scope is binding another collection and an
                        // IKeyRewriterValueProvider implementation was first (hiding the original "[key][next key]").
                        if (kvp.Value.EndsWith("]"))
                        {
                            bindingContext.ModelName = ModelNames.CreatePropertyModelName(prefix, kvp.Key);
                        }
                        else
                        {
                            bindingContext.ModelName = ModelNames.CreateIndexModelName(prefix, kvp.Key);
                        }

                        await _valueBinder.BindModelAsync(bindingContext);
                        valueResult = bindingContext.Result;
                    }

                    // Always add an entry to the dictionary but validate only if binding was successful.
                    model[convertedKey] = CastOrDefault<TValue>(valueResult.Model);
                    keyMappings.Add(bindingContext.ModelName, convertedKey);
                }
            }

            bindingContext.ValidationState.Add(model, new ValidationStateEntry()
            {
                Strategy = new ShortFormDictionaryValidationStrategy<TKey, TValue>(keyMappings, valueMetadata),
            });
        }
        internal static TModel CastOrDefault<TModel>(object model)
        {
            return (model is TModel) ? (TModel)model : default(TModel);
        }

        /// <inheritdoc />
        protected override object ConvertToCollectionType(
            Type targetType,
            IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            if (collection == null)
            {
                return null;
            }

            if (targetType.IsAssignableFrom(typeof(Dictionary<TKey, TValue>)))
            {
                // Collection is a List<KeyValuePair<TKey, TValue>>, never already a Dictionary<TKey, TValue>.
                return collection.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            return base.ConvertToCollectionType(targetType, collection);
        }

        /// <inheritdoc />
        protected override object CreateEmptyCollection(Type targetType)
        {
            if (targetType.IsAssignableFrom(typeof(Dictionary<TKey, TValue>)))
            {
                // Simple case such as IDictionary<TKey, TValue>.
                return new Dictionary<TKey, TValue>();
            }

            return base.CreateEmptyCollection(targetType);
        }

        public override bool CanCreateInstance(Type targetType)
        {
            if (targetType.IsAssignableFrom(typeof(Dictionary<TKey, TValue>)))
            {
                // Simple case such as IDictionary<TKey, TValue>.
                return true;
            }

            return base.CanCreateInstance(targetType);
        }
    }
}
