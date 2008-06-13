/*
Copyright (C) 2007 Nicholas Nystrom

This program is free software; you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation; either version 2 of the License, or (at your option) any later
version.

This program is distributed in the hope that it will be useful, but WITHOUT
ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with
this program; if not, write to the Free Software Foundation, Inc., 59 Temple
Place, Suite 330, Boston, MA 02111-1307 USA

http://nsprocs.sf.net
nnystrom@gmail.com
*/

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Xml;

namespace NSprocs
{
	public enum ProcedureReturnType
	{
		NotSpecified,
		Auto,
		SqlDataReader,
		DataSet,
		TypedDataSet,
		None
	}

	public class ProcedureOptions
	{
        public List<string> NullableParams { get; set; }
        public string Name { get; set; }
        public bool Ignore { get; set; }
        public ProcedureReturnType ReturnType { get; set; }
        public string TypedDataSet { get; set; }

	    public ProcedureOptions(
			XmlTextReader xml)
		{
			// Read Name
			Name = xml.GetAttribute("Name");
			if (string.Empty == Name ||
				Name.Length < 1)
			{
				throw new LineNumberedException(
					xml.LineNumber,
					xml.LinePosition,
					"You must specify a name for the stored procedure."
				);
			}
			
			// Read Ignore
			Ignore =
				xml.GetAttribute("Ignore") == "true" ?
				true:
				false;
			
			// Read ReturnType
			if (null == xml.GetAttribute("ReturnType"))
			{
				ReturnType = ProcedureReturnType.NotSpecified;
			}
			else
			{
				try
				{
					ReturnType = (ProcedureReturnType)Enum.Parse(
						typeof(ProcedureReturnType),
						xml.GetAttribute("ReturnType"),
						false);
				}
				catch
				{
					throw new LineNumberedException(
						xml.LineNumber,
						xml.LinePosition,
						"Invalid procedure return type."
					);
				}
			}

			// Read TypedDataset name
			TypedDataSet = xml.GetAttribute("TypedDataSet");
			if (null != TypedDataSet && String.Empty == TypedDataSet)
			{
				throw new LineNumberedException(
					xml.LineNumber,
					xml.LinePosition,
					"Invalid TypedDataset name. If specified, you cannot leave this parameter blank.");
			}

			// Read NullableParams
			NullableParams = new List<string>();
			var a = xml.GetAttribute("NullableParams");
			if (null != a)
			{
				NullableParams.AddRange(a.Split(','));
			}
	        ReturnType = ProcedureReturnType.Auto;
		}
	}

    public class MappingOption
    {
        public string Schema { get; private set; }
        public string Prefix { get; private set; }
        public string Class { get; private set; }

        public MappingOption(XmlReader xml)
        {
            Schema = xml.GetAttribute("Schema");
            Prefix = xml.GetAttribute("Prefix");
            Class = xml.GetAttribute("Class");
        }

        public bool Match(Signatures.ISignature sig)
        {
            return (String.IsNullOrEmpty(Schema) || sig.Schema == Schema) &&
                   (String.IsNullOrEmpty(Prefix) || sig.Name.StartsWith(Prefix));
        }
    }

	public class Options
	{
	    private ProcedureOptions _default;
        private readonly Dictionary<string, ProcedureOptions> _options = new Dictionary<string, ProcedureOptions>();

        public string ConnectionString { get; set; }
        public string SnippetPre { get; set; }
		public string SnippetPost { get; set; }
        public ProcedureReturnType AutoReturnType { get; set; }
        public List<MappingOption> Mappings { get; set; }
        public bool GenerateWarnings { get; set; }
        public bool ParseNames { get; set; }
	    public string ParseNamesPrefix { get; set; }
	    public string ParseNamesDelim { get;  set; }
	    public string RuntimeConnectionString { get; set; }
	    public string RuntimeConnectionExpression { get; set; }
	    public string ClassName { get; set; }
	    public bool IgnoreNonMatchingProcedures { get; set; }
        public string Language { get; set; }

		public Options(string xml)
			: this(new XmlTextReader(new StringReader(xml))) 
		{
		}

		public Options(XmlTextReader xml)
		{
            Mappings = new List<MappingOption>();
		    AutoReturnType = ProcedureReturnType.SqlDataReader;

			xml.WhitespaceHandling = WhitespaceHandling.None;
			while (xml.Read())
			{
				if (xml.NodeType == XmlNodeType.Element)
				{
					switch (xml.Name)
					{
                        case "Language":
					        Language = xml.GetAttribute("Value");
					        break;

						case "ConnectionString":
							ConnectionString = 
								xml.GetAttribute(
								"Value");
							break;

						case "RuntimeConnectionString":
							RuntimeConnectionString = 
								xml.GetAttribute(
								"Value");
							break;

                        case "RuntimeConnectionExpression":
                            RuntimeConnectionExpression =
                                xml.ReadInnerXml().Trim();
                            break;

						case "ClassName":
							ClassName = 
								xml.GetAttribute(
								"Value");
							break;

						case "StoredProcedure":
							ReadProcedureOptions(xml);
							break;

						case "Map":
							Mappings.Add(new MappingOption(xml));
							break;

						case "DefaultMapping":
							ParseNames = true;
							ParseNamesPrefix = xml.GetAttribute("Prefix");
							ParseNamesDelim = xml.GetAttribute("Delim");
							break;

						case "GenerateWarnings":
							GenerateWarnings = bool.Parse(xml.GetAttribute("Value"));
							break;

						case "SnippetPre":
							SnippetPre = xml.ReadString();
							break;

						case "SnippetPost":
							SnippetPost = xml.ReadString();
							break;

						case "AutoReturnType":
							AutoReturnType = (ProcedureReturnType)
								Enum.Parse(typeof(ProcedureReturnType), xml.GetAttribute("Value"), true);
							break;

						case "IgnoreNonMatchingProcedures":
							IgnoreNonMatchingProcedures = true;
							break;

					} // switch
				} // if
			} // for

			if (RuntimeConnectionString == String.Empty)
			{
				throw new Exception("No runtime connection specified.");
			}
			if (ClassName == String.Empty)
			{
				throw new Exception("No class name specified.");
			}
		    ParseNamesPrefix = "";
		    ParseNamesDelim = "_";
		}

        public SqlConnection CreateConnection()
        {
            return new SqlConnection(ConnectionString);
        }

        public ProcedureOptions this[string proc]
        {
            get
            {
                // look for the record
                return _options.ContainsKey(proc) ? _options[proc] : _default;
            }
        }

		private void ReadProcedureOptions(XmlTextReader xml)
		{
			// read the procedure def
			var po = new ProcedureOptions(xml);
	
			// is this the default?
			if (po.Name == "?")
			{
				if (_default != null)
				{
					throw new LineNumberedException(
						xml.LineNumber,
						xml.LinePosition,
						"You cannot include more than one default (Name=\"?\") stored procedures."
					);
				}
				_default = po;
			}
			else
			{
				_options[po.Name] = po;
			}
		}

		/// <summary>
		/// Compares a procedure name to our parameters and decides whether
		/// it should be ignored or processed.
		/// </summary>
		/// <param name="sig"></param>
		/// <returns></returns>
		public bool Match(Signatures.ISignature sig)
		{
			// match against a specific mapping
			foreach (var mo in Mappings)
            {
                if (mo.Match(sig)) return true;
			}

			// matches the default mapping?
			if (ParseNames && sig.Name.StartsWith(ParseNamesPrefix))
			{
				return true;
			}

			// doesnt match
			return !IgnoreNonMatchingProcedures;
		}
	}
}
