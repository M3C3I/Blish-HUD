﻿/*
 *  This code is heavily adapted from the Myra TextBox (https://github.com/rds1983/Myra/blob/a9dbf7a1ceedc19f9e416c754eaf38e89a89a746/src/Myra/Graphics2D/UI/TextBox.cs)
 *
 *  MIT License
 *
 *  Copyright (c) 2017-2020 The Myra Team
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a copy
 *  of this software and associated documentation files (the "Software"), to deal
 *  in the Software without restriction, including without limitation the rights
 *  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *  copies of the Software, and to permit persons to whom the Software is
 *  furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in all
 *  copies or substantial portions of the Software.

 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *  SOFTWARE.
 */

using System;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD.Controls.Resources;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using Microsoft.Xna.Framework.Input;

namespace Blish_HUD.Controls {

    /// <summary>
    /// Represents a textbox control.
    /// </summary>
    public abstract class TextInputBase : Control {

        protected static readonly Logger Logger = Logger.GetLogger<TextInputBase>();

        #region Load Static

        private static readonly Color _highlightColor;

        static TextInputBase() {
            _highlightColor = new Color(92, 80, 103, 150);
        }

        #endregion

        protected const char NEWLINE = '\n';

        public event EventHandler<EventArgs>           TextChanged;
        public event EventHandler<EventArgs>           EnterPressed;
        public event EventHandler<Keys>                KeyPressed;
        public event EventHandler<Keys>                KeyDown;
        public event EventHandler<Keys>                KeyUp;
        public event EventHandler<ValueEventArgs<int>> CursorIndexChanged;

        protected void OnCursorIndexChanged(ValueEventArgs<int> e) {
            _cursorMoved = true;

            UpdateScrolling();

            CursorIndexChanged?.Invoke(this, e);
        }

        protected void OnTextChanged(ValueChangedEventArgs<string> e) {
            TextChanged?.Invoke(this, e);
        }

        protected string _text = string.Empty;
        public string Text {
            get => _text;
            set => SetText(value, false);
        }

        private string UserText {
            get => _text;
            set => SetText(value, true);
        }

        protected string _placeholderText;
        public string PlaceholderText {
            get => _placeholderText;
            set => SetProperty(ref _placeholderText, value);
        }

        protected Color _foreColor = Color.FromNonPremultiplied(239, 240, 239, 255);
        public Color ForeColor {
            get => _foreColor;
            set => SetProperty(ref _foreColor, value);
        }

        protected BitmapFont _font = Content.DefaultFont14;
        public BitmapFont Font {
            get => _font;
            set => SetProperty(ref _font, value, true);
        }

        protected bool _focused = false;
        public bool Focused {
            get => _focused;
            set => SetProperty(ref _focused, value);
        }

        protected int _selectionStart;
        public int SelectionStart {
            get => _selectionStart;
            set => SetProperty(ref _selectionStart, value, true);
        }

        protected int _selectionEnd;
        public int SelectionEnd {
            get => _selectionEnd;
            set => SetProperty(ref _selectionEnd, value, true);
        }

        protected int _cursorIndex;
        public int CursorIndex {
            get => _cursorIndex;
            set {
                if (SetProperty(ref _cursorIndex, value, true)) {
                    OnCursorIndexChanged(new ValueEventArgs<int>(value));
                }
            }
        }
        
        private bool _wrapText;
        public bool WrapText {
            get => _wrapText;
            set => SetProperty(ref _wrapText, value, true);
        }

        public int Length => _text.Length;

        protected bool IsShiftDown         => GameService.Input.Keyboard.KeysDown.Contains(Keys.LeftShift) || GameService.Input.Keyboard.KeysDown.Contains(Keys.RightShift);
        protected bool IsCtrlDown          => GameService.Input.Keyboard.KeysDown.Contains(Keys.LeftControl) || GameService.Input.Keyboard.KeysDown.Contains(Keys.RightControl);
        protected int  CursorWidth         => System.Windows.Forms.SystemInformation.CaretWidth;
        protected int  CursorBlinkInterval => System.Windows.Forms.SystemInformation.CaretBlinkTime;

        protected bool _multiline;
        protected bool _caretVisible;
        protected bool _cursorMoved;

        private TimeSpan _lastInvalidate;
        private bool     _insertMode;
        private bool     _suppressRedoStackReset;

        private readonly UndoRedoStack _undoStack = new UndoRedoStack();
        private readonly UndoRedoStack _redoStack = new UndoRedoStack();

        public TextInputBase() {
            _lastInvalidate = DateTime.MinValue.TimeOfDay;

            Input.Mouse.LeftMouseButtonReleased += OnGlobalMouseLeftMouseButtonReleased;
            Input.Keyboard.KeyPressed           += OnGlobalKeyboardKeyPressed;
        }

        private void OnTextInput(string value) {
            bool ctrlDown = this.IsCtrlDown;

            foreach (char c in value) {
                if (char.IsControl(c)) continue;
                if (_font.GetCharacterRegion(c) == null) continue;

                if (ctrlDown) {
                    switch (c) {
                        case 'c':
                            HandleCopy();
                            return;
                        case 'x':
                            HandleCut();
                            return;
                        case 'v':
                            HandlePaste();
                            return;
                        case 'z':
                            HandleUndo();
                            return;
                        case 'y':
                            HandleRedo();
                            return;
                        case 'a':
                            SelectAll();
                            return;
                    }
                }

                InputChar(c);
            }
        }

        private void DeleteChars(int index, int length) {
            if (length <= 0) return;

            UserText = UserText.Substring(0, index) + UserText.Substring(index + length);
        }

        private string RemoveUnsupportedCharacters(string value) {
            foreach (char c in value) {
                if (_font.GetCharacterRegion(c) == null && !(_multiline && c == NEWLINE)) {
                    return RemoveUnsupportedCharacters(value.Replace(c.ToString(), string.Empty));
                }
            }

            return value;
        }

        private bool InsertChars(int index, string value, out int length) {
            if (string.IsNullOrEmpty(value)) {
                length = 0;
                return false;
            }

            value = RemoveUnsupportedCharacters(value);

            if (string.IsNullOrEmpty(_text)) {
                this.UserText = value;
            } else {
                this.UserText = this.UserText.Substring(0, index) + value + this.UserText.Substring(index);
            }

            length = value.Length;
            return true;
        }

        private bool InsertChar(int index, char value) {
            if (string.IsNullOrEmpty(_text)) {
                this.UserText = value.ToString();
            } else {
                this.UserText = UserText.Substring(0, index) + value + this.UserText.Substring(index);
            }

            return true;
        }

        public void Insert(int index, string value) {
            if (string.IsNullOrEmpty(value)) return;

            if (InsertChars(index, value, out int length) && length > 0) {
                _undoStack.MakeInsert(index, length);
                this.CursorIndex += length;
            }
        }

        public void Replace(int index, int length, string value) {
            if (length <= 0) {
                Insert(index, value);
                return;
            }

            if (string.IsNullOrEmpty(value)) {
                Delete(index, length);
                return;
            }

            _undoStack.MakeReplace(value, index, length, value.Length);
            this.UserText = this.UserText.Substring(0, index) + value + this.UserText.Substring(index + length);
        }

        public void ReplaceAll(string value) {
            Replace(0, 
                    string.IsNullOrEmpty(value) 
                        ? 0 
                        : value.Length,
                    value);
        }

        private bool Delete(int index, int length) {
            if (index < 0 || index >= this.Length || length < 0) return false;

            _undoStack.MakeDelete(_text, index, length);
            DeleteChars(index, length);

            return true;
        }

        private void DeleteSelection() {
            if (_selectionStart == _selectionEnd) return;

            if (_selectionStart < _selectionEnd) {
                Delete(_selectionStart, _selectionEnd - _selectionStart);
                this.SelectionEnd = _cursorIndex = _selectionStart;
            } else {
                Delete(_selectionEnd, _selectionStart - _selectionEnd);
                this.SelectionStart = _cursorIndex = _selectionEnd;
            }
        }

        private bool Paste(string value) {
            DeleteSelection();

            if (InsertChars(_cursorIndex, value, out var length) && length > 0) {
                _undoStack.MakeInsert(_cursorIndex, length);
                this.CursorIndex += length;
                return true;
            }

            return false;
        }

        private void InputChar(char value) {
            if (value == NEWLINE) {
                if (!_multiline) return;
            } else if (_font.GetCharacterRegion(value) == null) return;

            if (_insertMode && _selectionStart == _selectionEnd && _cursorIndex < this.Length) {
                _undoStack.MakeReplace(_text, _cursorIndex, 1, 1);
                DeleteChars(_cursorIndex, 1);

                if (InsertChar(_cursorIndex, value)) {
                    UserSetCursorIndex(_cursorIndex + 1);
                }
            } else {
                DeleteSelection();

                if (InsertChar(_cursorIndex, value)) {
                    _undoStack.MakeInsert(_cursorIndex, 1);
                    UserSetCursorIndex(_cursorIndex + 1);
                }
            }

            ResetSelection();
        }

        private void UndoRedo(UndoRedoStack undoStack, UndoRedoStack redoStack) {
            if (undoStack.Stack.Count == 0) return;

            var record = undoStack.Stack.Pop();

            try {
                _suppressRedoStackReset = true;

                switch (record.OperationType) {
                    case OperationType.Insert:
                        redoStack.MakeDelete(_text, record.Index, record.Length);
                        DeleteChars(record.Index, record.Length);
                        UserSetCursorIndex(record.Index);
                        break;
                    case OperationType.Delete:
                        if (InsertChars(record.Index, record.Data, out int length)) {
                            redoStack.MakeInsert(record.Index, length);
                            UserSetCursorIndex(record.Index + length);
                        }

                        break;
                    case OperationType.Replace:
                        redoStack.MakeReplace(_text, record.Index, record.Length, record.Data.Length);
                        DeleteChars(record.Index, record.Length);
                        InsertChars(record.Index, record.Data, out _);
                        break;
                }
            } finally {
                _suppressRedoStackReset = false;
            }

            ResetSelection();
        }

        protected void UserSetCursorIndex(int newIndex) {
            if (newIndex > this.Length) {
                newIndex = this.Length;
            }

            if (newIndex < 0) {
                newIndex = 0;
            }

            this.CursorIndex = newIndex;
        }

        protected void ResetSelection() {
            this.SelectionStart = _selectionEnd = _cursorIndex;
        }

        protected void UpdateSelection() {
            this.SelectionEnd = _cursorIndex;
        }

        private void UpdateSelectionIfShiftDown() {
            if (this.IsShiftDown) {
                UpdateSelection();
            } else {
                ResetSelection();
            }
        }

        protected void MoveLine(int delta) {

        }

        protected void SelectAll() {
            this.SelectionStart = 0;
            this.SelectionEnd   = this.Length;
        }

        private string ProcessText(string value) {
            value = value?.Replace("\r", string.Empty);

            if (!_multiline) {
                value = value?.Replace("\n", string.Empty);
            }

            return value;
        }

        protected bool SetText(string value, bool byUser) {
            string prevText = _text;

            value = ProcessText(value);

            if (!SetProperty(ref _text, value)) return false;

            // TODO: Update formatted text?

            if (!byUser) {
                this.CursorIndex = _selectionStart = _selectionEnd = 0;
            }

            if (!_suppressRedoStackReset) {
                _redoStack.Reset();
            }

            _cursorMoved = true;

            OnTextChanged(new ValueChangedEventArgs<string>(prevText, value));

            return true;
        }

        private void OnGlobalKeyboardKeyPressed(object sender, KeyboardEventArgs e) {
            if (!_focused && _enabled) return;

            bool ctrlDown  = this.IsCtrlDown;

            switch (e.Key) {
                case Keys.Insert:
                    _insertMode = !_insertMode;
                    break;
                case Keys.Left:
                    if (_cursorIndex > 0) {
                        UserSetCursorIndex(_cursorIndex - 1);
                        UpdateSelectionIfShiftDown();
                    }
                    break;
                case Keys.Right:
                    if (_cursorIndex < this.Length) {
                        UserSetCursorIndex(_cursorIndex + 1);
                        UpdateSelectionIfShiftDown();
                    }
                    break;
                case Keys.Up:
                    MoveLine(-1);
                    break;
                case Keys.Down:
                    MoveLine(1);
                    break;
                case Keys.Back:
                    HandleBackspace();
                    break;
                case Keys.Delete:
                    HandleDelete();
                    break;
                case Keys.Home:
                    HandleHome(ctrlDown);
                    break;
                case Keys.End:
                    HandleEnd(ctrlDown);
                    break;
                case Keys.Enter:
                    InputChar(NEWLINE);
                    break;
            }
        }

        protected void HandleCopy() {
            if (_selectionEnd != _selectionStart) {
                int selectStart = Math.Min(_selectionStart, _selectionEnd);
                int selectEnd   = Math.Max(_selectionStart, _selectionEnd);

                string clipboardText = _text.Substring(selectStart, selectEnd - selectStart);

                ClipboardUtil.WindowsClipboardService.SetTextAsync(clipboardText)
                             .ContinueWith((clipboardResult) => {
                                               if (clipboardResult.IsFaulted) {
                                                   Logger.Warn(clipboardResult.Exception, "Failed to set clipboard text to {clipboardText}!", clipboardText);
                                               }
                                           });
            }
        }

        protected void HandleCut() {
            HandleCopy();
            DeleteSelection();
        }

        protected void HandlePaste() {
            ClipboardUtil.WindowsClipboardService.GetTextAsync()
                         .ContinueWith((Task<string> clipboardTask) => {
                              if (!clipboardTask.IsFaulted) {
                                  if (!string.IsNullOrEmpty(clipboardTask.Result)) {
                                      Paste(clipboardTask.Result);
                                  }
                              } else {
                                 Logger.Warn(clipboardTask.Exception, "Failed to read clipboard text from system clipboard!");
                             }
                          });
        }

        protected void HandleUndo() {
            UndoRedo(_undoStack, _redoStack);
        }

        protected void HandleRedo() {
            UndoRedo(_redoStack, _undoStack);
        }

        protected void HandleBackspace() {
            if (_selectionStart == _selectionEnd) {
                if (Delete(_cursorIndex - 1, 1)) {
                    UserSetCursorIndex(_cursorIndex - 1);
                    ResetSelection();
                }
            } else {
                DeleteSelection();
            }
        }

        protected void HandleDelete() {
            if (_selectionStart == _selectionEnd) {
                Delete(_cursorIndex, 1);
            } else {
                DeleteSelection();
            }
        }

        protected void HandleHome(bool ctrlDown) {
            int newIndex = 0;

            if (!ctrlDown && !string.IsNullOrEmpty(_text)) {
                newIndex = _cursorIndex;

                while (newIndex > 0 && (newIndex - 1 >= this.Length || _text[newIndex - 1] != NEWLINE)) {
                    --newIndex;
                }
            }

            UserSetCursorIndex(newIndex);
            UpdateSelectionIfShiftDown();
        }

        protected void HandleEnd(bool ctrlDown) {
            int newIndex = this.Length;

            if (!ctrlDown) {
                while (newIndex < this.Length && _text[newIndex] != NEWLINE) {
                    ++newIndex;
                }
            }

            UserSetCursorIndex(newIndex);
            UpdateSelectionIfShiftDown();
        }

        protected abstract void UpdateScrolling();

        private void OnGlobalMouseLeftMouseButtonReleased(object sender, MouseEventArgs e) {
            this.Focused = _mouseOver && _enabled;

            if (_focused) {
                GameService.Input.Keyboard.SetTextInputListner(OnTextInput);
            } else  {
                GameService.Input.Keyboard.UnsetTextInputListner(OnTextInput);
                _undoStack.Reset();
                _redoStack.Reset();
            }
        }

        protected override CaptureType CapturesInput() { return CaptureType.Mouse; }

        protected void PaintText(SpriteBatch spriteBatch, Rectangle textRegion) {
            // Draw the placeholder text
            if (!_focused && this.Length == 0) {
                spriteBatch.DrawStringOnCtrl(this, _placeholderText, _font, textRegion, Color.LightGray, false, false, 0, HorizontalAlignment.Left, VerticalAlignment.Top);
            }

            // Draw the text
            spriteBatch.DrawStringOnCtrl(this, _text, _font, textRegion, _foreColor, false, false, 0, HorizontalAlignment.Left, VerticalAlignment.Top);
        }

        protected void PaintHighlight(SpriteBatch spriteBatch, Rectangle highlightRegion) {
            spriteBatch.DrawOnCtrl(this,
                                   ContentService.Textures.Pixel,
                                   highlightRegion,
                                   _highlightColor);
        }

        protected void PaintCursor(SpriteBatch spriteBatch, Rectangle cursorRegion) {
            if (_caretVisible) {
                spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, cursorRegion, _foreColor);
            }
        }

        public override void DoUpdate(GameTime gameTime) {
            // Determines if the blinking caret is currently visible
            _caretVisible = _focused && (Math.Round(gameTime.TotalGameTime.TotalSeconds) % 2 == 1 || gameTime.TotalGameTime.Subtract(_lastInvalidate).TotalSeconds < 0.75);

            if (_cursorMoved) {
                _lastInvalidate = gameTime.TotalGameTime;
                _cursorMoved = false;
            }
        }

    }
}
