﻿/*
Copyright (c) 2014 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;

namespace DScript
{
	public partial class ScriptEngine : IDisposable
	{
		#region IDisposable
		private bool _disposed;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_disposed)
			{
				if (disposing)
				{
					_stringClass.UnRef();
					_arrayClass.UnRef();
					_objectClass.UnRef();
					Root.UnRef();
				}

				// Indicate that the instance has been disposed.
				_disposed = true;
			}
		}
		#endregion

		private readonly ScriptVar _stringClass;
		private readonly ScriptVar _objectClass;
		private readonly ScriptVar _arrayClass;
		private IList<ScriptVar> _scopes;
		private readonly Stack<String> _callStack;

		private ScriptLex _currentLexer;

		public delegate void ScriptCallbackCB(ScriptVar var, object userdata);

		public ScriptVar Root { get; private set; }

		public ScriptEngine()
		{
			_currentLexer = null;

			_scopes = new List<ScriptVar>();
			_callStack = new Stack<string>();

			Root = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();

			_stringClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();
			_objectClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();
			_arrayClass = (new ScriptVar(null, ScriptVar.Flags.Object)).Ref();

			Root.AddChild("String", _stringClass);
			Root.AddChild("Object", _objectClass);
			Root.AddChild("Array", _arrayClass);
		}

		public void Trace()
		{
			//Root.Trace();
		}

		public void Execute(String code)
		{
			ScriptLex oldLex = _currentLexer;
			IList<ScriptVar> oldScopes = _scopes;

			using (_currentLexer = new ScriptLex(code))
			{
				_scopes.Clear();
				_scopes.Add(Root);
				_callStack.Clear();

				try
				{
					while (_currentLexer.TokenType != 0)
					{
						Statement(true);
					}
				}
				catch (ScriptException ex)
				{
					String errorMessage = "";
					int i = 0;
					foreach (ScriptVar scriptVar in _scopes)
					{
						errorMessage += "\n" + i++ + ": " + scriptVar;
					}
					throw new ScriptException(errorMessage, ex);
				}
			}

			_currentLexer = oldLex;
			_scopes = oldScopes;
		}

		public void AddNative(String funcProto, ScriptCallbackCB callback, Object userdata)
		{
			ScriptLex oldLex = _currentLexer;

			using (_currentLexer = new ScriptLex(funcProto))
			{
				ScriptVar baseVar = Root;

				_currentLexer.Match(ScriptLex.LexTypes.RFunction);
				String funcName = _currentLexer.TokenString;
				_currentLexer.Match(ScriptLex.LexTypes.Id);

				while (_currentLexer.TokenType == (ScriptLex.LexTypes) '.')
				{
					_currentLexer.Match((ScriptLex.LexTypes) '.');
					ScriptVarLink link = baseVar.FindChild(funcName);

					if (link == null)
					{
						link = baseVar.AddChild(funcName, new ScriptVar(null, ScriptVar.Flags.Object));
					}

					baseVar = link.Var;
					funcName = _currentLexer.TokenString;
					_currentLexer.Match(ScriptLex.LexTypes.Id);
				}

				ScriptVar funcVar = new ScriptVar(null, ScriptVar.Flags.Function | ScriptVar.Flags.Native);
				funcVar.SetCallback(callback, userdata);

				ParseFunctionArguments(funcVar);

				baseVar.AddChild(funcName, funcVar);
			}

			_currentLexer = oldLex;
		}

		private ScriptVarLink FindInScopes(String name)
		{
			foreach (ScriptVar scriptVar in _scopes)
			{
				ScriptVarLink a = scriptVar.FindChild(name);
				if (a != null)
				{
					return a;
				}
			}

			return null;
		}

		private ScriptVarLink FindInParentClasses(ScriptVar obj, String name)
		{
			ScriptVarLink implementation = null;
			ScriptVarLink parentClass = obj.FindChild(ScriptVar.PrototypeClassName);

			while (parentClass != null)
			{
				implementation = parentClass.Var.FindChild(name);
				if (implementation != null) return implementation;
				parentClass = parentClass.Var.FindChild(ScriptVar.PrototypeClassName);
			}

			if (obj.IsString)
			{
				implementation = _stringClass.FindChild(name);
				if (implementation != null) return implementation;
			}

			if (obj.IsArray)
			{
				implementation = _arrayClass.FindChild(name);
				if (implementation != null) return implementation;
			}

			implementation = _objectClass.FindChild(name);
			if (implementation != null) return implementation;

			return null;
		}

		private ScriptVarLink ParseFunctionDefinition()
		{
			_currentLexer.Match(ScriptLex.LexTypes.RFunction);
			String funcName = String.Empty;

			//named function
			if (_currentLexer.TokenType == ScriptLex.LexTypes.Id)
			{
				funcName = _currentLexer.TokenString;
				_currentLexer.Match(ScriptLex.LexTypes.Id);
			}

			ScriptVarLink funcVar = new ScriptVarLink(new ScriptVar(null, ScriptVar.Flags.Function), funcName);
			ParseFunctionArguments(funcVar.Var);

			Int32 funcBegin = _currentLexer.TokenStart;
			Block(false);
			funcVar.Var.SetData(_currentLexer.GetSubString(funcBegin));

			return funcVar;
		}

		private void ParseFunctionArguments(ScriptVar funcVar)
		{
			_currentLexer.Match((ScriptLex.LexTypes)'(');
			while (_currentLexer.TokenType != (ScriptLex.LexTypes)')')
			{
				funcVar.AddChildNoDup(_currentLexer.TokenString, null);
				_currentLexer.Match(ScriptLex.LexTypes.Id);

				if (_currentLexer.TokenType != (ScriptLex.LexTypes)')')
				{
					_currentLexer.Match((ScriptLex.LexTypes)',');
				}
			}

			_currentLexer.Match((ScriptLex.LexTypes)')');
		}

		private void Clean(ScriptVarLink link)
		{
			ScriptVarLink v = link;
			if (link != null && !link.Owned)
			{
				link.Dispose();
			}
		}
	}
}