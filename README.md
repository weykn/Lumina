# Lumina Language

Lumina is a lightweight dynamic programming language featuring full math expression support, unrestricted variable naming (numbers, emojis, symbols), live token deletion at runtime (even keywords and operators), inline function calls, and deep .NET interoperability via imports.

Designed for maximum flexibility, simplicity, and experimentation.

## Table of Contents
- [Directives](#directives)
- [Variables and Assignment](#variables-and-assignment)
- [Expressions Everywhere](#expressions-everywhere)
- [Supported Literals and Types](#supported-literals-and-types)
- [Built-in and Inline Calls](#built-in-and-inline-calls)
- [Function Calls](#function-calls)
- [Return and Exit Codes](#return-and-exit-codes)
- [Deleting Tokens](#deleting-tokens)
- [Error Handling](#error-handling)

---

## Directives

- `IMPORT "<path>"`  
  Load a .NET assembly for external function calls.

- `DEFINE <Name> … END`  
  Declare a new script function (including the required `MAIN` entrypoint).

---

## Variables and Assignment

- **Arbitrary names**:  
  Variables can be numbers, emojis, symbols, mixed-case, anything.  
  Examples:
  ```text
  X: 5
  3: 55
  😂: "hello world"
  ```

- **Colon syntax** for assignment:  
  ```text
  VariableName: <expression>
  ```

---

## Expressions Everywhere

- Numeric math operators:  
  `+`, `-`, `*`, `/`, `%`  

- Parentheses `(` `)` for grouping and precedence.  

- Full expressions allowed in:
  - Assignments
  - Inline calls
  - `RETURN` statements
  - Example:
    ```text
    X: (4 + 5 * 2)
    !PRINTLINE X / 3 + (Y - 1)
    RETURN 3 + 55
    ```

---

## Supported Literals and Types

- **Numbers**: e.g., `123`
- **Strings**: `"hello world"`
- **Booleans**: `TRUE`, `FALSE`

---

## Built-in and Inline Calls

- Syntax:
  ```text
  !FunctionName <expression> [<expression> ...]
  ```
- Built-in function:
  - `!PRINTLINE <value>` — prints the evaluated value(s) to console.

- Additional built-ins can be added to the `BuiltIns` map in the engine.

---

## Function Calls

- **Inline Call** (to script or external functions):
  ```text
  !FuncName arg1 arg2 ...
  ```
  - Arguments are evaluated expressions.

- **External Functions**:  
  From imported .NET DLLs (public static methods).

---

## Return and Exit Codes

- `RETURN <expression>`  
  Stops execution and exits the host process with the evaluated value as the exit code (must be numeric).

---

## Deleting Tokens

- `DELETE <token>`

  Deletes a token from the language.

  Behavior:
  - If a **variable** with that name exists → deletes the variable only.
  - If no such variable → **globally disables** the token.
    - Disabled tokens **cannot** be used again as:
      - Keywords
      - Operators (`+`, `-`, etc.)
      - Function names
      - Literals
      - Variables
      - Anything else

- **Examples:**
  ```text
  3: 55
  DELETE 3
  !PRINTLINE 3  # prints 3 (as literal after variable is deleted)

  DELETE RETURN
  # any future RETURN statement will error

  DELETE +
  # '+' operator is now invalid everywhere

  DELETE "hello"
  # string literal "hello" cannot be used anymore

  DELETE DELETE
  # even the DELETE command itself can be deleted!
  ```

---

## Error Handling

- Errors cause the program to immediately abort and print the error:
  - Use of undefined or deleted tokens
  - Divide-by-zero
  - Bad syntax (mismatched parentheses, invalid expressions)
  - Unknown function names
  - Improper function definitions (e.g., missing `END`)
