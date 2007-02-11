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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
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
		private string _name;
		private bool _ignore = false;
		private ProcedureReturnType _returnType = ProcedureReturnType.Auto;
		private StringCollection _nullableParams;
		private string _typedDatset;

		public ProcedureOptions(
			XmlTextReader xml)
		{
			// Read Name
			_name = xml.GetAttribute("Name");
			if (string.Empty == _name ||
				_name.Length < 1)
			{
				throw new LineNumberedException(
					xml.LineNumber,
					xml.LinePosition,
					"You must specify a name for the stored procedure."
				);
			}
			
			// Read Ignore
			_ignore =
				xml.GetAttribute("Ignore") == "true" ?
				true:
				false;
			
			// Read ReturnType
			if (null == xml.GetAttribute("ReturnType"))
			{
				_returnType = ProcedureReturnType.NotSpecified;
			}
			else
			{
				try
				{
					_returnType = (ProcedureReturnType)Enum.Parse(
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
			_typedDatset = xml.GetAttribute("TypedDataSet");
			if (null != _typedDatset && String.Empty == _typedDatset)
			{
				throw new LineNumberedException(
					xml.LineNumber,
					xml.LinePosition,
					"Invalid TypedDataset name. If specified, you cannot leave this parameter blank.");
			}

			// Read NullableParams
			_nullableParams = new StringCollection();
			string a = xml.GetAttribute("NullableParams");
			if (null != a)
			{
				_nullableParams.AddRange(a.Split(','));
			}
		}

		public StringCollection NullableParams
		{
			get
			{
				return _nullableParams;
			}
		}
		public string Name
		{
			get
			{
				return _name;
			}
		}
		public bool Ignore
		{
			get
			{
				return _ignore;
			}
		}
		public ProcedureReturnType ReturnType
		{
			get
			{
				return _returnType;
			}
		}
		public string TypedDataSet
		{
			get
			{
				return _typedDatset;
			}
		}
	}

    public class MappingOption
    {
        string _schema = String.Empty;
        string _prefix = String.Empty;
        string _class;

        public string Schema { get { return _schema; } }
        public string Prefix { get { return _prefix; } }
        public string Class { get { return _class; } }

        public MappingOption(XmlTextReader xml)
        {
            _schema = xml.GetAttribute("Schema");
            _prefix = xml.GetAttribute("Prefix");
            _class = xml.GetAttribute("Class");
        }

        public bool Match(Signatures.ISignature sig)
        {
            return (String.IsNullOrEmpty(_schema) || sig.Schema == _schema) &&
                   (String.IsNullOrEmpty(_prefix) || sig.Name.StartsWith(_prefix));
        }
    }

	public class Options
	{
		private string _connectionString;
		private string _runtimeConnectionString;
        private string _runtimeConnectionExpression;
		private string _className;
		private ProcedureOptions _default = null;
		private Hashtable _options = new Hashtable();
		private ProcedureReturnType _AutoReturnType = ProcedureReturnType.SqlDataReader;
		private string _SnippetPre = String.Empty;
		private string _SnippetPost = String.Empty;

		private bool _GenerateWarnings = true;

		private bool _IgnoreNonMatchingProcedures = false;

		private bool _ParseNames = false;
		private string _ParseNamesPrefix = "";
		private string _ParseNamesDelim = "_";
        private List<MappingOption> _Mappings = new List<MappingOption>();

		public string SnippetPre
		{
			get
			{
				return _SnippetPre;
			}
		}

		public string SnippetPost
		{
			get
			{
				return _SnippetPost;
			}
		}

		public ProcedureReturnType AutoReturnType
		{
			get
			{
				return _AutoReturnType;
			}
		}

		public List<MappingOption> Mappings
		{
			get
			{
				return _Mappings;
			}
		}

		public bool GenerateWarnings
		{
			get
			{
				return _GenerateWarnings;
			}
		}

		public bool ParseNames
		{
			get
			{
				return _ParseNames;
			}
		}

		public string ParseNamesPrefix
		{
			get
			{
				return _ParseNamesPrefix;
			}
		}

		public string ParseNamesDelim
		{
			get
			{
				return _ParseNamesDelim;
			}
		}

		public SqlConnection CreateConnection()
		{
			return new SqlConnection(_connectionString);
		}	

		public string RuntimeConnectionString
		{
			get
			{
				return _runtimeConnectionString;
			}
		}

        public string RuntimeConnectionExpression
        {
            get
            {
                return _runtimeConnectionExpression;
            }
        }

		public string ClassName
		{
			get
			{
				return _className;
			}
		}

		public bool IgnoreNonMatchingProcedures
		{
			get { return _IgnoreNonMatchingProcedures; }
		}
			
		public ProcedureOptions this[string proc]
		{
			get
			{
				// look for the record
				ProcedureOptions po =(ProcedureOptions)_options[proc];
				if (null == po)
				{
					// no specific record for this proc,
					// just return the default
					return _default;
				}
				else
				{
					return po;
				}
			}
		}

		public Options(string xml)
			: this(new XmlTextReader(new StringReader(xml))) 
		{
		}
		public Options(XmlTextReader xml)
		{
			xml.WhitespaceHandling = WhitespaceHandling.None;
			while (xml.Read())
			{
				if (xml.NodeType == XmlNodeType.Element)
				{
					switch (xml.Name)
					{
						case "ConnectionString":
							_connectionString = 
								xml.GetAttribute(
								"Value");
							break;

						case "RuntimeConnectionString":
							_runtimeConnectionString = 
								xml.GetAttribute(
								"Value");
							break;

                        case "RuntimeConnectionExpression":
                            _runtimeConnectionExpression =
                                xml.ReadInnerXml().Trim();
                            break;

						case "ClassName":
							_className = 
								xml.GetAttribute(
								"Value");
							break;

						case "StoredProcedure":
							ReadProcedureOptions(xml);
							break;

						case "Map":
							_Mappings.Add(new MappingOption(xml));
							break;

						case "DefaultMapping":
							_ParseNames = true;
							_ParseNamesPrefix = xml.GetAttribute("Prefix");
							_ParseNamesDelim = xml.GetAttribute("Delim");
							break;

						case "GenerateWarnings":
							_GenerateWarnings = bool.Parse(xml.GetAttribute("Value"));
							break;

						case "SnippetPre":
							_SnippetPre = xml.ReadString();
							break;

						case "SnippetPost":
							_SnippetPost = xml.ReadString();
							break;

						case "AutoReturnType":
							_AutoReturnType = (ProcedureReturnType)
								Enum.Parse(typeof(ProcedureReturnType), xml.GetAttribute("Value"), true);
							break;

						case "IgnoreNonMatchingProcedures":
							_IgnoreNonMatchingProcedures = true;
							break;

					} // switch
				} // if
			} // for

			if (_runtimeConnectionString == String.Empty)
			{
				throw new Exception("No runtime connection specified.");
			}
			if (_className == String.Empty)
			{
				throw new Exception("No class name specified.");
			}
		}

		private void ReadProcedureOptions(XmlTextReader xml)
		{
			// read the procedure def
			ProcedureOptions po = new ProcedureOptions(xml);
	
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
			foreach (MappingOption mo in Mappings)
            {
                if (mo.Match(sig)) return true;
			}

			// matches the default mapping?
			if (_ParseNames && sig.Name.StartsWith(_ParseNamesPrefix))
			{
				return true;
			}

			// doesnt match
			return !IgnoreNonMatchingProcedures;
		}
	}
}
