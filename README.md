# Lumina Language

Lumina is a lightweight, dynamic programming language featuring:

- Full math expression support everywhere  
- Unrestricted variable naming (numbers, emojis, symbols)  
- Live token deletion at runtime (even keywords and operators)  
- Inline function calls  
- Deep .NET interoperability via `IMPORT`  
- Direct execution start (statements run as they appear)  
- **Execution reversal** with `REVERSE` (reverse the flow upward)  

*Inspired by ideas from [GulfOfMexico (DreamBerd)](https://github.com/TodePond/GulfOfMexico) — credits!*

## Table of Contents

- [Directives](#directives)  
- [Functions](#functions)  
- [Top-level Statements](#top-level-statements)  
- [Variables and Assignment](#variables-and-assignment)  
- [Expressions Everywhere](#expressions-everywhere)  
- [Supported Literals and Types](#supported-literals-and-types)  
- [Built-in and Inline Calls](#built-in-and-inline-calls)  
- [Reversing Execution](#reversing-execution)  
- [Deleting Tokens](#deleting-tokens)  
- [Error Handling](#error-handling)  

---

## Directives

- `IMPORT "<path>"`  
  Loads a .NET assembly for external function calls.

---

## Functions

- **Keyword**: any subsequence of **FUNCTION** (case-insensitive):  
  e.g., `F`, `FN`, `FUNC`, `FCTION`, `FUNCTION`  
- **Syntax**:
  ```text
  <FnKeyword> <Name>
    …statements…
  END
  ```

- **Function names**:  
  Like variables, function names can be *anything* — numbers, emojis, symbols, etc.

  **Examples**:
  ```text
  F 😂
    !PRINTLINE "This is funny"
  END

  F 123
    !PRINTLINE "Number function"
  END

  F !
    !PRINTLINE "Exclamation function!"
  END
  ```

- **Calling**:
  ```text
  !😂
  !123
  !!
  ```

- **Notes**:
  - Functions only run when called.
  - No automatic `MAIN` function or entry point.

---

## Top-level Statements

Any statement outside a function runs immediately, in order:

```text
!PRINTLINE "Hello"
x: 10
!PRINTLINE x * 2
```

---

## Variables and Assignment

- **Arbitrary names**: numbers, emojis, symbols — anything is allowed.  
  ```text
  😂: "hello"
  3: 55
  $x!: 42
  ```
- **Assignment syntax**:
  ```text
  VariableName: <expression>
  ```

---

## Expressions Everywhere

- Operators: `+`, `-`, `*`, `/`, `%`  
- Parentheses for grouping: `(` `)`  
- Expressions appear in assignments, calls, `RETURN`, etc.

---

## Supported Literals and Types

- **Numbers**: e.g., `123`, `-5`, `0.75`
- **Strings**: `"hello world"`
- **Booleans**: `TRUE`, `FALSE`

---

## Built-in and Inline Calls

- **Inline call syntax**:
  ```text
  !FunctionName <expr> [<expr> …]
  ```
- **Built-in functions**:
  - `!PRINTLINE <value>` — prints evaluated value(s) to console  

- **External functions**:
  - Any public static method from imported .NET assemblies.

---

## Reversing Execution

- **`REVERSE`**  
  Flips the direction of top-level execution.  
  After `REVERSE`, code runs *upward* instead of downward.

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

**Explanation**:  
- First, it prints `1` and `2`.
- `REVERSE` is hit.
- Now execution goes *up*, printing `2` (again) and `1` (again).

*Yes — code may be visited more than once!*

---

Good catch.  
You're right — **deleting `FN`** (or any other function keyword) **only deletes that specific spelling**, but you could still define functions with other valid abbreviations (like `F`, `FUNC`, `FUNCTION`, etc.).

I'll fix that part cleanly in the docs to be 100% accurate.

Here’s the corrected part for **Deleting Tokens**:

---

## Deleting Tokens

- `DELETE <token>`  
  - If `<token>` is a **variable**, deletes only that variable.
  - Otherwise, **globally disables** `<token>` forever.

Disabled tokens cannot be reused as:
- Keywords (`RETURN`, `REVERSE`, **specific function spellings** like `FN`, etc.)
- Operators (`+`, `-`, etc.)
- Function names
- Literals
- Variables
- Anything

**Notes**:
- Deleting a **function keyword** like `FN` disables only that particular abbreviation.  
  - You can still use other valid versions (like `F`, `FUNC`, `FUNCTION`) until they are deleted too.

**Examples**:

```text
3: 55
DELETE 3
!PRINTLINE 3      # prints the literal number 3

DELETE RETURN
# any future RETURN will cause an error

DELETE +
# '+' operator is now invalid

DELETE FN
# cannot define functions with 'FN' anymore
# but 'F', 'FUNC', 'FUNCTION' still work unless deleted
```

---

## Error Handling

Any error (like undefined or deleted token usage, bad syntax, divide-by-zero, unknown function, etc.) immediately aborts execution with an error message.

---

**✅ Updated** exactly as you wanted — explained the full freedom for function names and added calling `!!`.  

Want me to also make a short **quickstart example** program (like 10 lines) at the bottom? It would help new users jump in even faster. 🚀  
Want it?