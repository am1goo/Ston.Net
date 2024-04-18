﻿using Ston.Serializers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Ston
{
    public class StonSerializer
    {
        private const char Separator    = ':';
        private const char Equal        = '=';
        private const char BraceStart   = '{';
        private const char BraceEnd     = '}';

        private IReadOnlyList<IStonConverter> _converters = new List<IStonConverter>
        {
            PrimitiveStonConverter.instance,
        };

        public Task<StonObject> DeserializeFileAsync(string filePath, StonSettings settings = default)
        {
            var fi = new FileInfo(filePath);
            return DeserializeFileAsync(fi, settings);
        }

        public async Task<StonObject> DeserializeFileAsync(FileInfo fileInfo, StonSettings settings = default)
        {
            if (!fileInfo.Exists)
                return null;

            using (var fs = fileInfo.OpenRead())
            {
                using (var sr = new StreamReader(fs))
                {
                    var ston = await sr.ReadToEndAsync();
                    return Deserialize(ston, settings);
                }
            }
        }

        public StonObject Deserialize(string ston, StonSettings settings = default)
        {
            if (settings == null)
                settings = StonSettings.defaultSettings;

            var obj = new StonObject();
            var lines = ston.Split(Environment.NewLine);
            var length = lines.Length;
            for (int i = 0; i < length; ++i)
            {
                var line = lines[i];

                var firstIndex = line.IndexOfAnySymbol();
                if (firstIndex < 0)
                    continue;

                if (line[firstIndex] == '#')
                    continue;

                var separatorIndex = line.IndexOf(Separator);
                if (separatorIndex < 0)
                {
                    if (TryParseObject(ref i, lines, settings, out var key, out var stonObject))
                    {
                        obj.Add(key, stonObject);
                    }
                }
                else
                {
                    if (TryParseValue(ref i, lines, settings, out var key, out var stonValue))
                    {
                        obj.Add(key, stonValue);
                    }
                }
            }
            return obj;
        }

        private bool TryParseObject(ref int index, string[] lines, StonSettings settings, out string key, out StonObject stonObject)
        {
            var line = lines[index];

            var equalIndex = line.IndexOf(Equal);
            if (equalIndex < 0)
            {
                key = null;
                stonObject = null;
                return false;
            }

            key = line.Substring(0, equalIndex, StonExtensions.SubstringOptions.Trimmed);

            var braceStartIndex = line.IndexOf(BraceStart);
            if (braceStartIndex < 0)
            {
                stonObject = null;
                return false;
            }

            var intent = 0;
            var parsed = default(StonObject);
            StonCache<StringBuilder>.Pop(out var sb);
            for (int i = index; i < lines.Length; ++i)
            {
                index = i;

                line = lines[i];

                var skip = false;
                for (int n = 0; n < line.Length; ++n)
                {
                    var c = line[n];
                    if (c == BraceStart)
                    {
                        skip = intent == 0;
                        intent++;
                    }
                    else if (c == BraceEnd)
                    {
                        intent--;
                        skip = intent == 0;
                    }
                }

                var append = !skip;
                if (append)
                {
                    if (sb.Length > 0)
                        sb.Append(Environment.NewLine);
                    sb.Append(line);
                }

                if (intent != 0)
                    continue;

                var ston = sb.ToString();
                parsed = Deserialize(ston, settings);
                break;
            }
            StonCache<StringBuilder>.Push(sb);

            if (parsed == null)
            {
                stonObject = default;
                return false;
            }

            stonObject = parsed;
            return true;
        }

        private bool TryParseValue(ref int index, string[] lines, StonSettings settings, out string key, out StonValue stonValue)
        {
            var line = lines[index];
            var separatorIndex = line.IndexOf(Separator);
            if (separatorIndex < 0)
                throw new Exception($"character '{Separator}' wasn't found in line {index}");

            key = line.Substring(0, separatorIndex, StonExtensions.SubstringOptions.Trimmed);

            var typeAndValueIndex = separatorIndex + 1;
            var typeAndValue = line.Substring(typeAndValueIndex, line.Length - typeAndValueIndex);

            var equalIndex = typeAndValue.IndexOf(Equal);

            var type = typeAndValue.Substring(0, equalIndex - 0, StonExtensions.SubstringOptions.Trimmed);
            if (!TryGetConverter(type, settings, out var converter))
                throw new Exception($"unsupported type '{type}'");

            var valueIndex = equalIndex + 1;
            var valueStr = typeAndValue.Substring(valueIndex, typeAndValue.Length - valueIndex);

            stonValue = converter.Deserialize(type, valueStr);
            if (stonValue == null)
                throw new Exception($"converter {converter} should to return {nameof(StonValue)} for key '{key}' and type '{type}'");

            return true;
        }

        private bool TryGetConverter(string type, StonSettings settings, out IStonConverter result)
        {
            if (TryGetConverter(_converters, type, out result))
                return true;

            if (TryGetConverter(settings.converters, type, out result))
                return true;

            return false;
        }

        private static bool TryGetConverter(IEnumerable<IStonConverter> list, string type, out IStonConverter result)
        {
            if (list == null)
            {
                result = default;
                return false;
            }

            foreach (var converter in list)
            {
                if (converter.CanConvert(type))
                {
                    result = converter;
                    return true;
                }
            }

            result = default;
            return false;
        }
    }
}
