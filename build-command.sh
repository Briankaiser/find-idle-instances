#!/bin/bash

keys="$(grep -A2 'hudl_farm' ~/.aws/credentials | sed -n -e 's/aws_access_key_id=\([A-Z]*\)/--accesskey \1/p' -e 's/aws_secret_access_key=\([A-Z0-9a-z]*\)/ --secretkey \1/p' | sed '/\n$/!N;s/\n//')"

usageOptions="{find|reboot} {stream|stream-med|stream-lrg|phoenix-autogen|phoenix-hls-clip|phoenix-privacy|phoenix-reel-concat|phoenix-html-effects|phoenix-timeline-item} {use1|apse2|euw1}"

command="dotnet run"
case "$1" in 
find)
	command+=" find"
	;;
reboot)
	command+=" reboot"
	;;
*)
	echo $"Usage $0 $usageOptions"
	exit 1
esac
command+=" $keys "
if [[ $2 =~ stream-* ]]; then
	command+="--securityGroup prod-streamworker"
elif [[ $2 =~ phoenix-* ]]; then
	command+="--securityGroup prod-$2"
fi

case "$2" in
stream-lrg)
	command+="-lrg --name prod-farm-stream-lrg"
	;;
stream-med)
	command+="-med --name prod-farm-stream-med"
	;;
stream)
	command+=" --name prod-farm-stream"
	;;
phoenix-*)
	command+=" --name prod-farm-$2"
	;;
*)
	echo $"Usage $0 $usageOptions"
	exit 1
esac

case "$3" in
apse2)
	command+="-apse2 --region ap-southeast-2"
	;;
euw1)
	command+="-euw1 --region eu-west-1"
	;;
use1)
	;;
*)
	echo $"Usage $0 {find|reboot} {stream|stream-med|stream-lrg|phoenix-reel-concat|phoenix-html-effects|phoenix-timeline-item} {use1|apse2|euw1}"
	exit 1
esac
echo "$command"
exit 0
