# mvn-rce

Example remote code execution using snyk-maven-plugin.

Just by running `mvn test` on this repository, Snyk CLI will run an unexpected command configured in the pom.xml.
If you don't notice this and just run `mvn`, it's possible to do something more dangerous than simply opening a calculator.

Note! only works on MacOS.
