# 🌟 Lumina Language

Lumina is a lightweight, dynamic, and chaotic programming language featuring:

- 🔥 Full math expression support everywhere  
- 🔥 Anything as variable and function names (numbers, emojis, symbols)  
- 🔥 Lifetimes for variables (line-based or time-based)  
- 🔥 Live token deletion at runtime (even keywords and operators)  
- 🔥 Deep .NET interoperability via `IMPORT`  
- 🔥 Execution reversal (`REVERSE`) to flip program flow upward  
- 🔥 Flexible string handling (quotes optional)  
- 🔥 Number names (`one`, `two`, etc.) are built-in  

*Credits to [GulfOfMexico](https://github.com/TodePond/GulfOfMexico) for inspiring some ideas.*

---

## 📚 Table of Contents

- [Directives](#-directives)
- [Functions](#-functions)
- [Top-level Statements](#-top-level-statements)
- [Variables, Assignment, and Lifetimes](#-variables-assignment-and-lifetimes)
- [Strings](#-strings)
- [Expressions Everywhere](#-expressions-everywhere)
- [Supported Literals and Types](#-supported-literals-and-types)
- [Built-in and Inline Calls](#-built-in-and-inline-calls)
- [Reversing Execution](#-reversing-execution)
- [Deleting Tokens](#-deleting-tokens)
- [Error Handling](#-error-handling)

---

## 📥 Directives

- `IMPORT "<path>"`  
  Load a .NET assembly to use external functions.

---

## 🛠️ Functions

- **Keyword**: any part of **FUNCTION** (case-insensitive):  
  (`F`, `FN`, `FUNC`, `FCTION`, `FUNCTION`, etc.)

- **Syntax**:
  ```text
  <FnKeyword> <Name>
    ...statements...
  END
  ```

- **Function names**:  
  Can be anything — numbers, emojis, symbols.

  **Examples**:
  ```text
  F 😂
    !PRINTLINE "Funny!"
  END

  F 123
    !PRINTLINE "Number fn"
  END

  F !
    !PRINTLINE "Exclamation!"
  END
  ```

- **Calling**:
  ```text
  !😂
  !123
  !!
  ```

- **Note**:  
  Functions only run when called. No automatic `MAIN`.

---

## 🏃 Top-level Statements

Anything outside a function runs **immediately** when encountered:

```text
!PRINTLINE "Hello world"
x: 10
!PRINTLINE x * 2
```

---

## 🪄 Variables, Assignment, and Lifetimes

### Assignment
- **Syntax**:
  ```text
  Name: <expression>
  ```

- **Anything can be a name**:  
  Numbers, emojis, symbols, full sentences.

  ```text
  😂: "hello"
  3: 55
  $x!: 42
  ```
  
### Lifetimes
- Variables can **expire** automatically:

  - `X 2: 5` → exists for **2 lines**
  - `B 5s: "hey"` → exists for **5 seconds**
  - `B -3: '''bye'''` → existed 3 lines ago, deleted **now**

  **Examples**:
  ```text
  X 2: 5          # 2 lines lifetime
  B 5s: "hello"   # 5 seconds lifetime
  B -3: '''goodbye''' # retroactive deletion
  ```

---

## ✨ Strings

- **Multi-quote support**:  
  Triple, quadruple, or any number of `"` or `'` is valid.
- **No quotes needed**:  
  If a matching variable exists, it's used automatically.

  **Examples**:
  ```text
  """HELLO"""   # real string

  !PRINTLINE hello
  # if variable "hello" exists, it's used
  ```

---

## ➗ Expressions Everywhere

- Math operators: `+`, `-`, `*`, `/`, `%`
- Grouping: `(` `)`  
- Expressions appear **everywhere** — assignments, returns, function calls.

---

## 📏 Supported Literals and Types

- **Numbers**: `123`, `-7`, `3.14`
- **Strings**: `"hello"`, `"""triple quoted"""`, `''''any number''''`
- **Booleans**: `TRUE`, `FALSE`
- **Number names** (automatic mappings):
  ```text
  one   → 1
  two   → 2
  three → 3
  ```

  **Example**:
  ```text
  !PRINTLINE one       # prints 1
  !PRINTLINE two+two   # prints 4
  ```

---

## ⚡ Built-in and Inline Calls

- **Call syntax**:
  ```text
  !FunctionName <expr> [<expr> ...]
  ```

- **Built-in**:
  - `!PRINTLINE <value>` → prints evaluated value(s)

- **External**:
  - Any public static method from imported .NET assemblies.

---

## 🔄 Reversing Execution

- `REVERSE` flips the program execution direction.

Example:

```text
!PRINTLINE 1
!PRINTLINE 2
REVERSE
!PRINTLINE 3
!PRINTLINE 4
```

**Result**:
```text
1
2
2
1
```

**How it works**:  
- Prints `1`, then `2`.
- `REVERSE` is hit.
- Now runs upward: `2`, then `1` again.

*Yes — REVERSE can cause multiple visits!*

---

## 🚫 Deleting Tokens

- `DELETE <token>`  
  - If a **variable**, deletes only that instance.
  - Otherwise, **globally disables** the token forever.

Disabled tokens cannot be reused as:
- Keywords (`RETURN`, `REVERSE`, `FUNCTION`, etc.)
- Operators (`+`, `-`, etc.)
- Function names
- Literals
- Variables
- Anything

### Special:
- You can `DELETE DELETE` itself.  
  After that, no more deletions are possible.

**Examples**:
```text
3: 55
DELETE 3
!PRINTLINE 3      # prints literal 3

DELETE RETURN
# RETURN now disabled

DELETE +
# '+' operator invalid forever

DELETE FN
# can't define functions with 'FN' anymore

DELETE DELETE
# now DELETE is gone too
```

---

## ❗ Error Handling

- Any error — undefined token, deleted keyword, bad syntax, math errors — **instantly aborts** execution with an error message.
