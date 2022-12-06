<h1 align="center">
  <a href="https://raw.githubusercontent.com/svbygoibear/go-deps-with-govendor/master/img/go_govendor.png"><img src="https://raw.githubusercontent.com/svbygoibear/go-deps-with-govendor/master/img/go_govendor.png" alt="go-deps-with-govendor" width="100"></a>
  <br>
  go-deps-with-govendor
  <br>
</h1>

<h4 align="center">A sample of how to use govendor to manage dependencies.</h4>
<br>
<p align="center">
    <img src="https://raw.githubusercontent.com/svbygoibear/go-deps-with-govendor/master/img/go_govendor.gif" alt="go-deps-with-govendor">
</p>
<br>

## Introduction
With so many different ways to manage dependencies in Go, [govendor](https://github.com/kardianos/govendor) allows you to make use of all your dependencies in your `$GOPATH`, your `vendor` directory and implement a configuration file to keep your dependency versions consistent across all projects.

## Why use govendor?
Govendor is the perfect tool to use for dependency management if you want a really flexible workflow when it comes to how you handle dependencies and you want the best of both worlds by using vendoring and the standard `$GOPATH`. Since it keeps configuration on your dependencies it means you do not have to commit all your dependencies either, and it is extremely feature rich allowing you to have full control over what you use in your project.

## Pros and Cons
### Pros
- You get to use both the `$GOPATH` and `vendor` folders.
- You get to lock in dependencies at a package level.
- When committing code, you only need to provide the configuration file in the `vendor` folder and not the whole contents of it.

### Cons
- Same as with most other version dependency management tools in Go, you do not have the control to specify a specific release when updating a dependency.
- Since dependencies are managed at a package level, you can't recursively remove unused packages. This has to be done by hand. See [this](https://github.com/kardianos/govendor/issues/138) issue.
- It is a very open tool which can lead to workflow complexity as there's various different options on how to use it.

## Setting Up

### Get Go-ing
To use this sample, you will need [Go](https://golang.org) installed on your computer. **The steps documented in this project works with version 1.5.0 and up of Go as we make use of `vendoring`.**
Follow the instructions from the [official page](https://golang.org/doc/install). 

After you've completed the installation and ran a quick test, check your go version from terminal:
```
$ go version
```
You'll see something like this printed out:
```
go version go1.8.3 darwin/amd64
```
### Workspace Structure
When getting started with Go, you'll notice that your workspace needs to have a specific structure - just to highlight this again, it is a good idea to keep up this pattern:
```
$HOME
    |_ go
        |_ bin
        |_ pkg
        |_ src
            |_ go-deps-with-govendor
```
For more on workspaces, you can look at the [golang guide](https://golang.org/doc/code.html#Workspaces).

### Go-Verning your Vendors
#### Getting govendor
To get `govendor` on your machine, you can use Go to install it in your `$GOPATH`
```
$ go get -u github.com/kardianos/govendor
```

You'll now see govendor in your `bin` as well as in your `src` directories:
```
$HOME
    |_ go
        |_ bin
            |_ govendor
        |_ pkg
        |_ src
            |_ go-deps-with-govendor
            |_ github.com
                |_ kardianos
                    |_ govendor
```

#### Using govendor
Fist you'll need to initialise govendor for new projects (but it is not needed for this sample) by running init in the terminal:
```
$HOME/go/src/go-deps-with-govendor govendor init
```

If you get the following error response:
```
command not found: govendor
```
You might have to set your path again to have the correct `$GOPATH` and `$PATH`:
```
$HOME/go/src/go-deps-with-govendor export GOPATH=$HOME/go
$HOME/go/src/go-deps-with-govendor export PATH=$PATH:$GOPATH/bin
```

For this project, you'll need to fetch the dependencies you're using:
```
$HOME/go/src/go-deps-with-govendor govendor fetch github.com/labstack/echo
```
Which will add the dependencies to your local `vendor` directory and update your `vendor.json` file.

If you wanted to get a specific package, you can also specify this:
```
$HOME/go/src/go-deps-with-govendor govendor fetch github.com/labstack/echo/gommon
```

You can also add dependencies from the `$GOPATH`, in which case you will use the add command (if it is a global common library you want to use):
```
$HOME/go/src/go-deps-with-govendor govendor add github.com/labstack/echo
```

#### Updating Dependencies
Updating dependencies work exactly the same as installing new ones, and you can reference the release tag if you want a specific version:
```
$HOME/go/src/go-deps-with-govendor govendor fetch github.com/labstack/echo@v3.2.1
```
Just remember this version number will not be recorded in your `vendor.json` file though, but the correct tree reference in the `vendor.json` file will be updated and the specified version will be added to your `vendor` folder.

### IDE Specific Settings
#### IntelliJ Idea
All explanations up until this point has been done with terminal in mind. It is possible to set up this project in IntelliJ as well. 
First thing is to download and install the go-lang-idea-plugin:
<img src="https://raw.githubusercontent.com/svbygoibear/go-deps-with-govendor/master/img/go-lang-plugin.png" alt="go-deps-with-govendor" width="500">

Follow the steps outlined by the [go-lang-idea-plugin](https://github.com/go-lang-plugin-org/go-lang-idea-plugin/wiki/Documentation#setting-up-the-go-sdk).

## Using Project
After cloning this repository, you'll need to make sure that:
- You have the correct version of Go installed.
- You have your workspace set up.
- You have govendor installed.
- You've installed all your dependencies using fetch as needed, and global dependencies in in your `$GOPATH/src`.

The next step is to see if your project builds, whereby you can make use of the regular Go build command in terminal:
```
$HOME/go/src/go-deps-with-govendor go build
```

You can also run your Go project as you normally would:
```
$HOME/go/src/go-deps-with-govendor go run main.go
```

Additionally, you can add `-v` to either of those commands to see what imports is being used, e.g:
```
$HOME/go/src/go-deps-with-govendor go run -v main.go
```
Will produce:
```
go-deps-with-govendor/vendor/github.com/mattn/go-isatty
go-deps-with-govendor/vendor/golang.org/x/crypto/acme
go-deps-with-govendor/vendor/github.com/valyala/bytebufferpool
go-deps-with-govendor/vendor/github.com/mattn/go-colorable
go-deps-with-govendor/vendor/github.com/valyala/fasttemplate
go-deps-with-govendor/vendor/github.com/labstack/gommon/color
go-deps-with-govendor/vendor/github.com/labstack/gommon/log
go-deps-with-govendor/vendor/golang.org/x/crypto/acme/autocert
go-deps-with-govendor/vendor/github.com/labstack/echo
```

## Further Reading
- [Govendor](https://github.com/kardianos/govendor)
- [Getting Started](https://zerokspot.com/weblog/2017/04/23/getting-started-with-govendor/)
- [How to Use](https://nanxiao.gitbooks.io/golang-101-hacks/content/posts/use-govendor-to-implement-vendoring.html)
- [Go Vendoring Beginner Tutorial](https://gocodecloud.com/blog/2016/03/29/go-vendoring-beginner-tutorial/)
