## Snowflake Ezpaarse Upload

The source code in this repository is an extension of the following tool [ezpaarse-loader](https://github.com/ulsdevteam/ezpaarse-loader/).
It takes parsed ezpaarse files from the filesystem and uploads them to a Snowflake table, while also creating an association between a 
particular record and the responcibility center and department associated with said record.

## Quick Start

### From a release (recommended)
1. Grab the latest release

```bash
wget https://www.github.com/ulsdevteam/snowflake_ezpaarse_upload/releases/latest/download/ezpaarse_upload-release-0.1.0.tar.gz \ 
    -O snowflake_ezpaarse_upload.tar.gz
```

2. Decompress tarball and add execute permissions (if not set)
```bash
tar -xvzf snowflake_ezpaarse_upload.tar.gz
chmod +x snowflake_ezpaarse_upload
```

3. Setup environment variable (ideally using an env file)
```
======.env=========
export SNOWFLAKE_AUTH=
export BASE_DIR=
====================

source .env
```

4. Run the commands
```
./snowflake_ezpaarse_upload process
./snowflake_ezpaarse_upload postprocess
```

### From Source
0. Make sure you have a dotnet compiler

1. Grab source
```
git clone https://github.com/ulsdevteam/snowflake_ezpaarse_upload.git 
```

Note that main is usually stable, but for users seeking more stability or looking to get consistent results w.r.t binary, 
you can checkout a tag 

```
git clone https://github.com/ulsdevteam/snowflake_ezpaarse_upload.git 

git checkout 0.1.0-alpha # current latest tag
```

2. Compile the code
```
dotnet publish -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true
```

3. Copy the following files into execution environment (say, `/usr/local/snowflake_ezpaarse_upload`)
```
# cd bin/release/<dotnet_version>/<OS_String>/publish/
cd bin/release/net10.0/linux-x64/publish/
sudo mkdir -p /usr/local/snowflake_ezpaarse_upload/
sudo cp *.so snowflake_ezpaarse_upload /usr/local/snowflake_ezpaarse_upload/
cd /usr/local/snowflake_ezpaarse_upload/
```

4. Setup environment variable (ideally using an env file)
```
======.env=========
export SNOWFLAKE_AUTH=
export BASE_DIR=
====================

source .env
```

5. Run the commands
```
./snowflake_ezpaarse_upload process
./snowflake_ezpaarse_upload postprocess
```

## Subcommands

- **./snowflake_ezpaarse_upload process**:
  - Looks for files in `$BASE_DIR/pending/` and uploads their content to Snowflake table
  - On success, files are moved to `$BASE_DIR/done/`, and data can be found in table `EZPAARSE_RESULTS` (schema determined by `$SNOWFLAKE_AUTH_STRING`)
  - On failure, files and intermediate files are left in `$BASE_DIR/working/`, for manual intervention
  - Files already in `$BASE_DIR/working/` or `$BASE_DIR/done/` are skipped
  
- **./snowflake_ezpaarse_upload postprocess**
  - Updates the table `EZPAARSE_RESULT_DEPTS` with department codes and responsibility codes for newly inserted records (also handles sponsored accounts)
  - Sets `user_hash` column of newly inserted rows of `EZPAARSE_RESUTLS` using `$SALT`

## Environment Variables Used

- **SNOWFLAKE_AUTH_STRING**: 
  - Authentication string to connect to snowflake instance
  - Example: `account=<snowflake account>;user=<username>;password=<access_token>;db=<DB_NAME>;schema=<SCHEMA>`
  - The schema is assumed to have the tables `EZPAARSE_RESULTS` and `EZPAARSE_RESULT_DEPTS`

- **BASE_DIR**:
  - Directory with folders `/pending/`, `/working`, `/done/`. `/pending/` is assumed to be regualarly updated with output of [ezpaarse-loader](https://github.com/ulsdevteam/ezpaarse-loader/)
  - Defaults to current working directory

- **SALT**
  - Arbitray UTF8 string used for generating `user_hash`. 
  - SHA256 hexdigest of provided string is used as actual salt to prevent brute force attacks for salt

## Notes for users outside of Pitt

The sql commands used are hard coded into the binary, and rely on schemas internal to Pitt. For the code to be useful, it is 
recommended that custom sql be written as required and recompiled from source. Current sql code can be used as basis/inspiration. SQL commands live 
in the following places

- For **process.cs**: `CommandList.GetCommands()` and `CommandList.GetRecoveryCommands()`

- For **postprocess.cs**: `Main().exeString` variable

## Copyright/License
 * Copyright University of Pittsburgh
 * Licensed under GPL v2, or (at your option) any later version.
 * Maintained by ULS Systems Development
