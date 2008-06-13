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
using System.CodeDom;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;

using NSprocs.Signatures.SqlServer;

namespace NSprocs.Generators.SqlServer
{
    internal class Generator
    {
        public Options Options { get; set; }
        public SqlSignatures Signatures { get; set; }
        public CodeNamespace Root { get; set; }

        public Generator(
            Options Options,
            SqlSignatures Signatures)
        {
            this.Options = Options;
            this.Signatures = Signatures;
        }

        public CodeNamespace GenerateCode(
            string FileNamespace)
        {
            // Create the namespace
            Root = new CodeNamespace { Name = FileNamespace };

            // Import namespaces
            Root.Imports.Add(new CodeNamespaceImport("System"));
            Root.Imports.Add(new CodeNamespaceImport("System.Collections"));
            Root.Imports.Add(new CodeNamespaceImport("System.Data"));
            Root.Imports.Add(new CodeNamespaceImport("System.Data.SqlClient"));
            Root.Imports.Add(new CodeNamespaceImport("System.Data.SqlTypes"));

            // Create class
            var Class = new CodeTypeDeclaration
            {
                Attributes = MemberAttributes.Public,
                IsClass = true,
                Name = Options.ClassName
            };

            // When ParseNames is turned on, we store sub-classes here
            var Classes = new Dictionary<string, CodeTypeDeclaration>();

            // Create constructor
            Class.Members.Add(new CodeConstructor { Attributes = MemberAttributes.Private });

            // Add utility methods
            GenerateUtils(Class);

            // Add a method for each procedure
            foreach (Signature s in Signatures)
            {
                // build the regular and transacted versions
                var methodPlain =
                    GenerateProcedureMethod(
                    s,
                    Options[s.FrameworkName],
                    false);
                var methodTransacted =
                    GenerateProcedureMethod(
                    s,
                    Options[s.FrameworkName],
                    true);

                // can we find a mapping for this procedure?
                var methodName = methodPlain.Name;
                string className = null;
                MappingOption map = null;
                foreach (var mo in Options.Mappings)
                {
                    if (mo.Match(s))
                    {
                        map = mo;
                        methodName = string.IsNullOrEmpty(map.Prefix) ? methodName : methodName.Substring(map.Prefix.Length);
                        className = map.Class;
                        break;
                    }
                }
                if (null == map && Options.ParseNames)
                {
                    // no mapping found, try default mapping
                    if (methodName.StartsWith(Options.ParseNamesPrefix))
                    {
                        // strip the prefix
                        methodName = methodName.Substring(Options.ParseNamesPrefix.Length);

                        // look for the deliminator
                        var x = methodName.IndexOf(Options.ParseNamesDelim);
                        if (-1 != x)
                        {
                            className = methodName.Substring(0, x);
                            methodName = methodName.Substring(x + 1);
                        }
                    }
                }
                if (String.IsNullOrEmpty(className))
                {
                    // add methods to our top level class
                    methodPlain.Name = methodName;
                    methodTransacted.Name = methodName;
                    Class.Members.Add(methodPlain);
                    Class.Members.Add(methodTransacted);
                }
                else
                {
                    // we found a mapping
                    _AddMethodsToClass(
                        Class,
                        Classes,
                        methodName,
                        className,
                        methodPlain,
                        methodTransacted);
                }
            }

            // Add class to namespace
            Root.Types.Add(Class);

            return Root;
        }

        private static void _AddMethodsToClass(
            CodeTypeDeclaration Class,
            IDictionary<string, CodeTypeDeclaration> Classes,
            string MethodName,
            string ClassName,
            CodeTypeMember methodPlain,
            CodeTypeMember methodTransacted)
        {
            // have we already made this class?
            CodeTypeDeclaration c;
            if (Classes.ContainsKey(ClassName))
            {
                c = Classes[ClassName];
            }
            else
            {
                // create a new class
                c = new CodeTypeDeclaration
                        {
                            IsClass = true,
                            Name = ClassName,
                            Attributes = MemberAttributes.Public
                        };
                Classes[ClassName] = c;

                // add the class type declaration to the base class
                Class.Members.Add(c);
            }

            // rename the methods
            methodPlain.Name = MethodName;
            methodTransacted.Name = MethodName;

            // add the methods to the class
            c.Members.Add(methodPlain);
            c.Members.Add(methodTransacted);
        }

        private CodeMemberMethod GenerateProcedureMethod(
            Signature s,
            ProcedureOptions po,
            bool Transacted)
        {
            // Create the method
            var m = new CodeMemberMethod
                        {
                            Name = s.Name,
                            Attributes = (MemberAttributes.Public | MemberAttributes.Static)
                        };

            // Figure the actual return type
            var rt = po.ReturnType;
            if (ProcedureReturnType.NotSpecified == po.ReturnType)
            {
                rt = ProcedureReturnType.Auto;
            }
            if (ProcedureReturnType.Auto == po.ReturnType)
            {
                if (po.TypedDataSet != null)
                {
                    rt = ProcedureReturnType.TypedDataSet;
                }
                else if (s.ResultSets != null &&
                    s.ResultSets.Count > 0)
                {
                    rt = Options.AutoReturnType;
                }
                else
                {
                    rt = ProcedureReturnType.None;
                }
            }

            // Set return type
            switch (rt)
            {
                case ProcedureReturnType.TypedDataSet:
                    if (string.IsNullOrEmpty(po.TypedDataSet))
                    {
                        throw new Exception("The return type was specified as TypedDataSet, but no TypedDataSet name was specified.");
                    }
                    m.ReturnType = new CodeTypeReference(po.TypedDataSet);
                    break;

                case ProcedureReturnType.None:
                    m.ReturnType = null;
                    break;

                default:
                    Type t;
                    switch (rt)
                    {
                        case ProcedureReturnType.DataSet:
                            t = typeof(DataSet);
                            break;
                        case ProcedureReturnType.SqlDataReader:
                            t = typeof(SqlDataReader);
                            break;
                        default:
                            throw new Exception("Unknown Procedure Return Type.");
                    }
                    m.ReturnType = new CodeTypeReference(t);
                    break;
            }

            // Set parameters
            if (Transacted)
            {
                m.Parameters.Add(
                    new CodeParameterDeclarationExpression(
                        typeof(SqlTransaction), "trs"));
            }
            foreach (Parameter p in s.Parameters)
            {
                var pde =
                    new CodeParameterDeclarationExpression(
                        po.NullableParams.Contains(p.Name) ?
                            p.SqlType :
                            p.FrameworkType,
                        p.FrameworkName);
                if (p.Type == "output")
                {
                    pde.Direction = FieldDirection.Out;
                }
                m.Parameters.Add(pde);
            }

            // if this signature generated an error, place a 
            // #warning here
            if (s.Exception != null &&
                Options.GenerateWarnings)
            {
                // this will generate a compile warning in c#, as it should,
                // but will cause a syntax error in VB. Too bad vb doesnt have any
                // type of precompiler warning syntax.
                var errmsg = s.Exception.Message.Replace('\r', ' ').Replace('\n', ' ');
                if (errmsg.Length > 200)
                {
                    errmsg = errmsg.Substring(0, 200);
                }
                m.Statements.Add(
                    new CodeSnippetStatement(
                        String.Format("#warning {0}: \"{1}\"", s.Name, errmsg)
                ));
            }

            // Add any userdefined code to the beginning of the method
            if (Options.SnippetPre != String.Empty)
            {
                m.Statements.Add(new CodeSnippetStatement(Options.SnippetPre));
            }

            // Create SqlParameter expressions
            var l = new List<CodeExpression>();
            var outputs = new CodeStatementCollection();
            var assigns = new CodeStatementCollection();
            for (var i = 0; i < s.Parameters.Count; i++)
            {
                var p = (Parameter)s.Parameters[i];
                l.Add(
                    new CodeObjectCreateExpression(
                        typeof(SqlParameter),
                        new[] {
							new CodePrimitiveExpression(p.Name),
							(p.Type == "input")
                                ? new CodeArgumentReferenceExpression(p.FrameworkName)
                                : ((CodeExpression) new CodeFieldReferenceExpression(
													new CodeTypeReferenceExpression(typeof(SqlDbType)), p.SqlDbType.ToString())
								)
						}
                    )
                );
                if ("output" == p.Type)
                {
                    // add statments to set the direction to output
                    outputs.Add(
                        new CodeAssignStatement(
                            new CodePropertyReferenceExpression(
                                new CodeIndexerExpression(
                                    new CodeVariableReferenceExpression("parms"),
                                    new CodePrimitiveExpression(i)
                                ),
                                "Direction"
                            ),
                            new CodeFieldReferenceExpression(
                                new CodeTypeReferenceExpression(typeof(ParameterDirection)),
                                "Output"
                            )
                        )
                    );

                    // character and binary types need the output size set
                    var t = p.SqlDbType;
                    if (t == SqlDbType.Char ||
                        t == SqlDbType.VarChar ||
                        t == SqlDbType.NChar ||
                        t == SqlDbType.NVarChar ||
                        t == SqlDbType.Binary ||
                        t == SqlDbType.VarBinary)
                    {
                        outputs.Add(
                            new CodeAssignStatement(
                                new CodePropertyReferenceExpression(
                                    new CodeIndexerExpression(
                                        new CodeVariableReferenceExpression("parms"),
                                        new CodePrimitiveExpression(i)
                                    ),
                                    "Size"
                                ),
                                new CodePrimitiveExpression(p.Size)
                            )
                        );
                    }

                    // we need to pass the value back to the out param
                    // is this a 'nullable' parameter?
                    if (po.NullableParams.Contains(p.Name))
                    {
                        // [argument] = ReadSqlABC(parms[i]);
                        string method;
                        var tt = p.SqlType;
                        if (tt == typeof(SqlDateTime))
                        {
                            method = "ReadSqlDateTime";
                        }
                        else if (tt == typeof(SqlInt32))
                        {
                            method = "ReadSqlInt32";
                        }
                        else if (tt == typeof(SqlMoney))
                        {
                            method = "ReadSqlMoney";
                        }
                        else if (tt == typeof(SqlString))
                        {
                            method = "ReadSqlString";
                        }
                        else if (tt == typeof(SqlGuid))
                        {
                            method = "ReadSqlGuid";
                        }
                        else
                        {
                            throw new Exception("Cannot read nullable sql type: " + tt);
                        }
                        assigns.Add(
                            new CodeAssignStatement(
                                new CodeArgumentReferenceExpression(p.FrameworkName),
                                new CodeMethodInvokeExpression(
                                    new CodeTypeReferenceExpression(Options.ClassName),
                                    method,
                                    new CodeIndexerExpression(
                                        new CodeVariableReferenceExpression("parms"),
                                        new CodePrimitiveExpression(i)
                                    )
                                )
                            )
                        );
                    }
                    else
                    {
                        // [argument] = (type)parms[i].Value;
                        assigns.Add(
                            new CodeAssignStatement(
                                new CodeArgumentReferenceExpression(p.FrameworkName),
                                new CodeCastExpression(
                                    p.FrameworkType,
                                    new CodePropertyReferenceExpression(
                                        new CodeIndexerExpression(
                                            new CodeVariableReferenceExpression("parms"),
                                            new CodePrimitiveExpression(i)
                                        ),
                                        "Value"
                                    )
                                )
                            )
                        );
                    }
                }
            }

            // Create parms array
            m.Statements.Add(
                new CodeVariableDeclarationStatement(
                    new CodeTypeReference(
                        new CodeTypeReference(typeof(SqlParameter)),
                        1),
                    "parms",
                    new CodeArrayCreateExpression(
                        typeof(SqlParameter),
                        l.ToArray())
                )
            );

            // Add any parms[n].Direction = ... statements
            m.Statements.AddRange(outputs);

            // Create the SqlHelper parameters.. always the same
            var parms = new CodeExpression[] {
				new CodePrimitiveExpression(s.DatabaseName),
				new CodeVariableReferenceExpression("parms")
			};
            if (Transacted)
            {
                parms = new[] {
				    new CodeArgumentReferenceExpression("trs"),
                    parms[0],
                    parms[1]
                };
            }

            // Run the sproc
            switch (rt)
            {
                case ProcedureReturnType.TypedDataSet:
                    m.Statements.Add(
                        new CodeVariableDeclarationStatement(
                            new CodeTypeReference(po.TypedDataSet),
                            "ds",
                            new CodeObjectCreateExpression(
                                new CodeTypeReference(po.TypedDataSet))
                            )
                        );
                    m.Statements.Add(
                        new CodeMethodInvokeExpression(
                            null,
                            "FillDataSet",
                            Transacted
                                ? new[] {
                                   parms[0],
                                   parms[1],
                                   parms[2],
                                   new CodeVariableReferenceExpression("ds") }
                                : new[] {
                                   parms[0],
                                   parms[1],
                                   new CodeVariableReferenceExpression("ds") }
                            )
                        );
                    m.Statements.Add(
                        new CodeMethodReturnStatement(
                            new CodeVariableReferenceExpression("ds")
                            )
                        );
                    break;

                case ProcedureReturnType.DataSet:
                    m.Statements.Add(
                        new CodeMethodReturnStatement(
                            new CodeMethodInvokeExpression(
                                null,
                                "ExecuteDataSet",
                                parms
                                )
                            )
                        );
                    break;

                case ProcedureReturnType.SqlDataReader:
                    m.Statements.Add(
                        new CodeMethodReturnStatement(
                            new CodeMethodInvokeExpression(
                                null,
                                "ExecuteDataReader",
                                parms
                                )
                            )
                        );
                    break;

                case ProcedureReturnType.None:
                    m.Statements.Add(
                        new CodeMethodInvokeExpression(
                            null,
                            "ExecuteNonQuery",
                            parms
                            )
                        );
                    m.Statements.AddRange(assigns);
                    break;

                default:
                    throw new Exception("Unknown Procedure Return Type.");
            }

            // Add any userdefined code to the end of the method
            if (Options.SnippetPost != String.Empty)
            {
                m.Statements.Add(new CodeSnippetStatement(Options.SnippetPost));
            }

            return m;
        }

        private void GenerateUtils(CodeTypeDeclaration Class)
        {
            // ExecuteXXXX
            if (String.IsNullOrEmpty(Options.RuntimeConnectionExpression))
            {
                Class.Members.Add(GenerateCreateConnection());
            }
            Class.Members.Add(GenerateExecuteDataReader(true));
            Class.Members.Add(GenerateExecuteDataReader(false));
            Class.Members.Add(GenerateExecuteDataSet(true));
            Class.Members.Add(GenerateExecuteDataSet(false));
            Class.Members.Add(GenerateExecuteNonQuery(true));
            Class.Members.Add(GenerateExecuteNonQuery(false));
            Class.Members.Add(GenerateExecuteFillDataSet(true));
            Class.Members.Add(GenerateExecuteFillDataSet(false));

            // ReadSqlDateTime
            Class.Members.Add(
                GenerateReadSqlTypeFromDataRow("ReadSqlDateTime", typeof(SqlDateTime), typeof(DateTime)));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlDataReader("ReadSqlDateTime", typeof(SqlDateTime), "GetSqlDateTime"));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlParameter("ReadSqlDateTime", typeof(SqlDateTime), typeof(DateTime)));

            // ReadSqlInt32
            Class.Members.Add(
                GenerateReadSqlTypeFromDataRow("ReadSqlInt32", typeof(SqlInt32), typeof(int)));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlDataReader("ReadSqlInt32", typeof(SqlInt32), "GetSqlInt32"));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlParameter("ReadSqlInt32", typeof(SqlInt32), typeof(int)));

            // ReadSqlMoney
            Class.Members.Add(
                GenerateReadSqlTypeFromDataRow("ReadSqlMoney", typeof(SqlMoney), typeof(decimal)));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlDataReader("ReadSqlMoney", typeof(SqlMoney), "GetSqlMoney"));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlParameter("ReadSqlMoney", typeof(SqlMoney), typeof(decimal)));

            // ReadSqlString
            Class.Members.Add(
                GenerateReadSqlTypeFromDataRow("ReadSqlString", typeof(SqlString), typeof(string)));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlDataReader("ReadSqlString", typeof(SqlString), "GetSqlString"));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlParameter("ReadSqlString", typeof(SqlString), typeof(string)));

            // ReadSqlGuid
            Class.Members.Add(
                GenerateReadSqlTypeFromDataRow("ReadSqlGuid", typeof(SqlGuid), typeof(Guid)));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlDataReader("ReadSqlGuid", typeof(SqlGuid), "GetSqlGuid"));
            Class.Members.Add(
                GenerateReadSqlTypeFromSqlParameter("ReadSqlGuid", typeof(SqlGuid), typeof(Guid)));
        }

        #region Execute Helpers

        private CodeMemberMethod GenerateCreateConnection()
        {
            var m = new CodeMemberMethod
                        {
                            Name = "CreateConnection",
                            Attributes = (MemberAttributes.Static | MemberAttributes.Public),
                            ReturnType = new CodeTypeReference(typeof (SqlConnection))
                        };
            m.Statements.Add(
                new CodeMethodReturnStatement(
                    new CodeObjectCreateExpression(
                        typeof(SqlConnection),
                        new CodeSnippetExpression(Options.RuntimeConnectionString)
                    )
                )
            );
            return m;
        }

        CodeExpression GenerateGetConnectionExpression()
        {
            if (String.IsNullOrEmpty(Options.RuntimeConnectionExpression))
            {
                return new CodeMethodInvokeExpression(null, "CreateConnection");
            }
            else
            {
                return new CodeSnippetExpression(Options.RuntimeConnectionExpression);
            }
        }

        CodeMemberMethod __GenerateExecuteMethod(string name, bool Transacted)
        {
            var m = new CodeMemberMethod
                        {
                            Name = name,
                            Attributes = (MemberAttributes.Static | MemberAttributes.Public),
                            ReturnType = null
                        };

            // install parameters
            if (Transacted)
            {
                m.Parameters.Add(new CodeParameterDeclarationExpression(typeof(SqlTransaction), "Transaction"));
            }
            m.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), "StoredProcedure"));
            m.Parameters.Add(new CodeParameterDeclarationExpression(typeof(SqlParameter[]), "ProcedureParameters"));

            // create a command
            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlConnection),
                "c",
                Transacted
                    ? new CodePropertyReferenceExpression(new CodeArgumentReferenceExpression("Transaction"), "Connection")
                    : GenerateGetConnectionExpression()
            ));
            if (!Transacted)
            {
                m.Statements.Add(
                    new CodeConditionStatement(
                        new CodeBinaryOperatorExpression(
                            new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("c"), "State"),
                            CodeBinaryOperatorType.IdentityInequality,
                            new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(typeof(ConnectionState)), "Open")),
                        new CodeExpressionStatement(
                            new CodeMethodInvokeExpression(new CodeVariableReferenceExpression("c"), "Open")
                        )
                    )
                );
            }
            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlCommand),
                "cmd",
                new CodeMethodInvokeExpression(
                    new CodeVariableReferenceExpression("c"),
                    "CreateCommand")
            ));
            if (Transacted)
            {
                m.Statements.Add(new CodeAssignStatement(
                    new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "Transaction"),
                    new CodeArgumentReferenceExpression("Transaction")
                ));
            }
            m.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "CommandType"),
                new CodeFieldReferenceExpression(
                        new CodeTypeReferenceExpression(typeof(CommandType)),
                        "StoredProcedure")
            ));
            m.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "CommandText"),
                new CodeArgumentReferenceExpression("StoredProcedure")
            ));
            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(new CodeVariableReferenceExpression("cmd"), "Parameters"),
                "AddRange",
                new CodeArgumentReferenceExpression("ProcedureParameters")
            ));


            return m;
        }

        CodeMemberMethod GenerateExecuteNonQuery(bool Transacted)
        {
            CodeMemberMethod m = __GenerateExecuteMethod("ExecuteNonQuery", Transacted);

            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("cmd"),
                "ExecuteNonQuery"
            ));

            return m;
        }

        CodeMemberMethod GenerateExecuteDataSet(bool Transacted)
        {
            CodeMemberMethod m = __GenerateExecuteMethod("ExecuteDataSet", Transacted);
            m.ReturnType = new CodeTypeReference(typeof(DataSet));

            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(DataSet),
                "ds",
                new CodeObjectCreateExpression(typeof(DataSet))
            ));
            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlDataAdapter),
                "a",
                new CodeObjectCreateExpression(
                    typeof(SqlDataAdapter),
                    new CodeVariableReferenceExpression("cmd"))
            ));
            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("a"),
                "Fill",
                new CodeVariableReferenceExpression("ds")
            ));
            m.Statements.Add(new CodeMethodReturnStatement(
                new CodeVariableReferenceExpression("ds")
            ));

            return m;
        }

        CodeMemberMethod GenerateExecuteDataReader(bool Transacted)
        {
            var m = __GenerateExecuteMethod("ExecuteDataReader", Transacted);
            m.ReturnType = new CodeTypeReference(typeof(SqlDataReader));

            var parms = new List<CodeExpression>();
            if (!Transacted)
            {
                parms.Add(new CodeFieldReferenceExpression(
                        new CodeTypeReferenceExpression(typeof(CommandBehavior)),
                        "CloseConnection"));
            }

            m.Statements.Add(new CodeMethodReturnStatement(
                new CodeMethodInvokeExpression(
                    new CodeVariableReferenceExpression("cmd"),
                    "ExecuteReader",
                    parms.ToArray())
            ));

            return m;
        }

        CodeMemberMethod GenerateExecuteFillDataSet(bool Transacted)
        {
            var m = __GenerateExecuteMethod("ExecuteFillDataSet", Transacted);

            m.Parameters.Add(new CodeParameterDeclarationExpression(
                typeof(DataSet),
                "ds"
            ));

            m.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(SqlDataAdapter),
                "a",
                new CodeObjectCreateExpression(
                    typeof(SqlDataAdapter),
                    new CodeVariableReferenceExpression("cmd"))
            ));
            m.Statements.Add(new CodeMethodInvokeExpression(
                new CodeVariableReferenceExpression("a"),
                "Fill",
                new CodeArgumentReferenceExpression("ds")
            ));

            return m;
        }

        #endregion

        private static CodeMemberMethod GenerateReadSqlTypeFromDataRow(
            string Name,
            Type Type,
            Type FrameworkType)
        {
            var m = new CodeMemberMethod
                        {
                            Name = Name,
                            Attributes = (MemberAttributes.Static | MemberAttributes.Public)
                        };
            m.Parameters.Add(
                new CodeParameterDeclarationExpression(typeof(DataRow), "row"));
            m.Parameters.Add(
                new CodeParameterDeclarationExpression(typeof(String), "c"));
            m.ReturnType = new CodeTypeReference(Type);

            m.Statements.Add(
                new CodeConditionStatement(
                new CodeMethodInvokeExpression(
                new CodeArgumentReferenceExpression("row"),
                "IsNull",
                new CodeArgumentReferenceExpression("c")),
                new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodePropertyReferenceExpression(
										new CodeTypeReferenceExpression(Type),
										"Null"
										))
									},
                new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodeObjectCreateExpression(
										Type,
										new CodeCastExpression(
										FrameworkType,
										new CodeIndexerExpression(
										new CodeArgumentReferenceExpression("row"),
										new CodeArgumentReferenceExpression("c")
										))))
									}
                )
                );

            return m;
        }

        private static CodeMemberMethod GenerateReadSqlTypeFromSqlDataReader(
            string Name,
            Type Type,
            string GetMethod)
        {
            var m = new CodeMemberMethod
                        {
                            Name = Name,
                            Attributes = (MemberAttributes.Static | MemberAttributes.Public)
                        };
            m.Parameters.Add(
                new CodeParameterDeclarationExpression(typeof(SqlDataReader), "rs"));
            m.Parameters.Add(
                new CodeParameterDeclarationExpression(typeof(String), "c"));
            m.ReturnType = new CodeTypeReference(Type);

            m.Statements.Add(
                new CodeMethodReturnStatement(
                    new CodeMethodInvokeExpression(
                        new CodeArgumentReferenceExpression("rs"),
                        GetMethod,
                        new CodeMethodInvokeExpression(
                            new CodeArgumentReferenceExpression("rs"),
                            "GetOrdinal",
                            new CodeArgumentReferenceExpression("c")
                ))));

            return m;
        }

        private static CodeMemberMethod GenerateReadSqlTypeFromSqlParameter(
            string Name,
            Type Type,
            Type FrameworkType)
        {
            var m = new CodeMemberMethod
                        {
                            Name = Name,
                            Attributes = (MemberAttributes.Static | MemberAttributes.Public)
                        };
            m.Parameters.Add(
                new CodeParameterDeclarationExpression(typeof(SqlParameter), "p"));
            m.ReturnType = new CodeTypeReference(Type);

            m.Statements.Add(
                new CodeConditionStatement(
                    new CodeBinaryOperatorExpression(
                        new CodePropertyReferenceExpression(
                            new CodeArgumentReferenceExpression("p"),
                            "Value"),
                        CodeBinaryOperatorType.IdentityEquality,
                        new CodePropertyReferenceExpression(
                            new CodeTypeReferenceExpression(typeof(DBNull)),
                            "Value")
                    ),

                new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodePropertyReferenceExpression(
										new CodeTypeReferenceExpression(Type),
										"Null"
										))
									},

                new CodeStatement[] {
										new CodeMethodReturnStatement(
										new CodeObjectCreateExpression(
										Type,
										new CodeCastExpression(
										FrameworkType,
										new CodePropertyReferenceExpression(
											new CodeArgumentReferenceExpression("p"),
											"Value"
										))))
									}
                )
            );
            return m;
        }
    }
}